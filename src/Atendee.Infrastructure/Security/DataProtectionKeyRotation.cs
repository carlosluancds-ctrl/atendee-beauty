using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography.X509Certificates;

namespace Atendee.Infrastructure.Security;

/// <summary>
/// Configuration for coordinated Data Protection key rotation.
/// </summary>
public sealed class DataProtectionKeyRotationOptions
{
    public const string SectionName = "DataProtection:KeyRotation";

    /// <summary>
    /// Enable automated key rotation.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Key lifetime before rotation is triggered (default: 90 days).
    /// </summary>
    public TimeSpan KeyLifetime { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Grace period for old keys to remain available for decryption after rotation (default: 30 days).
    /// </summary>
    public TimeSpan OldKeyGracePeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Path to the new certificate for key encryption (for rotation).
    /// </summary>
    public string? NewCertificatePath { get; set; }

    /// <summary>
    /// Password for the new certificate.
    /// </summary>
    public string? NewCertificatePassword { get; set; }

    /// <summary>
    /// Whether to run key rotation check on startup.
    /// </summary>
    public bool CheckOnStartup { get; set; } = true;

    /// <summary>
    /// Minimum time between rotation checks (default: 24 hours).
    /// </summary>
    public TimeSpan MinimumCheckInterval { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Service for coordinated Data Protection key rotation with old-key compatibility.
/// Manages key lifecycle, automated rotation, and provides decryption support for old keys during grace period.
/// </summary>
public interface IDataProtectionKeyRotationService
{
    /// <summary>
    /// Checks if key rotation is needed and performs it if configured.
    /// </summary>
    Task<KeyRotationResult> CheckAndRotateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about current key ring status.
    /// </summary>
    Task<KeyRingStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually triggers key rotation (for operational use).
    /// </summary>
    Task<KeyRotationResult> RotateNowAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a key rotation operation.
/// </summary>
public sealed record KeyRotationResult(
    bool RotationPerformed,
    string? NewKeyId,
    string? PreviousKeyId,
    DateTimeOffset RotationTimeUtc,
    string? ErrorMessage);

/// <summary>
/// Status information about the Data Protection key ring.
/// </summary>
public sealed record KeyRingStatus(
    int TotalKeys,
    int ActiveKeys,
    int ExpiredKeys,
    int RevokedKeys,
    DateTimeOffset? LastRotationUtc,
    DateTimeOffset? NextRotationUtc,
    string? CurrentKeyId,
    bool RotationEnabled);

/// <summary>
/// V102: Coordinated Data Protection key rotation with old-key decryption support.
/// Runs as a background service, checks key age, rotates when lifetime exceeded,
/// and maintains old keys for decryption during grace period.
/// </summary>
public sealed class DataProtectionKeyRotationService : BackgroundService, IDataProtectionKeyRotationService
{
    private readonly ILogger<DataProtectionKeyRotationService> _logger;
    private readonly DataProtectionKeyRotationOptions _options;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private DateTimeOffset _lastCheckTime = DateTimeOffset.MinValue;
    private readonly object _lock = new();

    public DataProtectionKeyRotationService(
        ILogger<DataProtectionKeyRotationService> logger,
        IOptions<DataProtectionKeyRotationOptions> options,
        IDataProtectionProvider dataProtectionProvider,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _dataProtectionProvider = dataProtectionProvider ?? throw new ArgumentNullException(nameof(dataProtectionProvider));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Data Protection key rotation is disabled");
            return;
        }

        _logger.LogInformation("Data Protection key rotation service starting. Key lifetime: {KeyLifetime}, Grace period: {GracePeriod}",
            _options.KeyLifetime, _options.OldKeyGracePeriod);

        // Initial check on startup
        if (_options.CheckOnStartup)
        {
            try
            {
                await CheckAndRotateAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Initial key rotation check failed");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.MinimumCheckInterval, stoppingToken);
                await CheckAndRotateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during key rotation check");
            }
        }

        _logger.LogInformation("Data Protection key rotation service stopping");
    }

    public async Task<KeyRotationResult> CheckAndRotateAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (DateTimeOffset.UtcNow - _lastCheckTime < _options.MinimumCheckInterval)
            {
                return new KeyRotationResult(
                    false, null, null, DateTimeOffset.UtcNow,
                    "Minimum check interval not elapsed");
            }
            _lastCheckTime = DateTimeOffset.UtcNow;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var keyManager = scope.ServiceProvider.GetRequiredService<IKeyManager>();
            var allKeys = keyManager.GetAllKeys();

            var now = DateTimeOffset.UtcNow;
            var activeKeys = allKeys.Where(k => k.ExpirationDate > now).ToList();
            var expiredKeys = allKeys.Where(k => k.ExpirationDate <= now).ToList();

            // Check if current key is past its lifetime
            var currentKey = activeKeys.OrderByDescending(k => k.ActivationDate).FirstOrDefault();
            if (currentKey == null)
            {
                return new KeyRotationResult(
                    false, null, null, now,
                    "No active key found in key ring");
            }

            var keyAge = now - currentKey.ActivationDate;
            if (keyAge < _options.KeyLifetime)
            {
                _logger.LogDebug("Current key age {KeyAge} is less than lifetime {KeyLifetime}, no rotation needed",
                    keyAge, _options.KeyLifetime);
                return new KeyRotationResult(
                    false, currentKey.KeyId.ToString(), null, now, null);
            }

            _logger.LogInformation("Current key {KeyId} age {KeyAge} exceeds lifetime {KeyLifetime}, initiating rotation",
                currentKey.KeyId, keyAge, _options.KeyLifetime);

            // Perform rotation by creating a new key (old key remains for decryption during grace period)
            return await PerformRotationAsync(scope, currentKey, now, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Key rotation check failed");
            return new KeyRotationResult(
                false, null, null, DateTimeOffset.UtcNow, ex.Message);
        }
    }

    private async Task<KeyRotationResult> PerformRotationAsync(
        IServiceScope scope,
        IKey currentKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            var keyManager = scope.ServiceProvider.GetRequiredService<IKeyManager>();

            // Create new key - old key remains in key ring for decryption during grace period
            // ASP.NET Core Data Protection automatically retains expired keys for decryption
            IKey newKey;
            if (!string.IsNullOrWhiteSpace(_options.NewCertificatePath))
            {
                var cert = await LoadCertificateAsync(_options.NewCertificatePath, _options.NewCertificatePassword, cancellationToken);
                newKey = keyManager.CreateNewKey(
                    activationDate: now,
                    expirationDate: now.Add(_options.KeyLifetime));
                // Note: Certificate-based key protection would require additional configuration
            }
            else
            {
                newKey = keyManager.CreateNewKey(
                    activationDate: now,
                    expirationDate: now.Add(_options.KeyLifetime));
            }

            _logger.LogInformation("Key rotation completed. New key: {NewKeyId}, Previous key: {PreviousKeyId} (retained for decryption during grace period)",
                newKey.KeyId, currentKey.KeyId);

            // Schedule cleanup of old keys after grace period
            _ = Task.Run(async () => await CleanupExpiredKeysAsync(scope, now.Add(_options.OldKeyGracePeriod), cancellationToken), cancellationToken);

            return new KeyRotationResult(
                true,
                newKey.KeyId.ToString(),
                currentKey.KeyId.ToString(),
                now,
                null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Key rotation failed");
            return new KeyRotationResult(
                false, null, currentKey.KeyId.ToString(), now, ex.Message);
        }
    }

    private async Task<X509Certificate2?> LoadCertificateAsync(
        string path,
        string? password,
        CancellationToken cancellationToken)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                Path.GetFullPath(path),
                password,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load certificate from {Path}", path);
            return null;
        }
    }

    private async Task CleanupExpiredKeysAsync(
        IServiceScope scope,
        DateTimeOffset cleanupAfter,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(cleanupAfter - DateTimeOffset.UtcNow, cancellationToken);

            var keyManager = scope.ServiceProvider.GetRequiredService<IKeyManager>();
            var allKeys = keyManager.GetAllKeys();
            var now = DateTimeOffset.UtcNow;

            var keysToRemove = allKeys
                .Where(k => k.ExpirationDate <= now && k.IsRevoked)
                .ToList();

            foreach (var key in keysToRemove)
            {
                try
                {
                    // Note: ASP.NET Core Data Protection doesn't have a direct "delete key" API
                    // Keys are managed by the key ring; expired+revoked keys are eventually cleaned up
                    _logger.LogDebug("Key {KeyId} is expired and revoked, eligible for cleanup", key.KeyId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up key {KeyId}", key.KeyId);
                }
            }

            _logger.LogInformation("Key cleanup completed. {Count} keys eligible for removal", keysToRemove.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Key cleanup failed");
        }
    }

    public async Task<KeyRingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var keyManager = scope.ServiceProvider.GetRequiredService<IKeyManager>();
            var allKeys = keyManager.GetAllKeys();

            var now = DateTimeOffset.UtcNow;
            var activeKeys = allKeys.Where(k => k.ExpirationDate > now).ToList();
            var expiredKeys = allKeys.Where(k => k.ExpirationDate <= now).ToList();
            var revokedKeys = allKeys.Where(k => k.IsRevoked).ToList();

            var currentKey = activeKeys.OrderByDescending(k => k.ActivationDate).FirstOrDefault();
            var nextRotation = currentKey?.ActivationDate.Add(_options.KeyLifetime);

            return new KeyRingStatus(
                TotalKeys: allKeys.Count,
                ActiveKeys: activeKeys.Count,
                ExpiredKeys: expiredKeys.Count,
                RevokedKeys: revokedKeys.Count,
                LastRotationUtc: allKeys.OrderByDescending(k => k.ActivationDate).FirstOrDefault()?.ActivationDate,
                NextRotationUtc: nextRotation,
                CurrentKeyId: currentKey?.KeyId.ToString(),
                RotationEnabled: _options.Enabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get key ring status");
            return new KeyRingStatus(0, 0, 0, 0, null, null, null, _options.Enabled);
        }
    }

    public async Task<KeyRotationResult> RotateNowAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _lastCheckTime = DateTimeOffset.MinValue; // Force check
        }
        return await CheckAndRotateAsync(cancellationToken);
    }
}

