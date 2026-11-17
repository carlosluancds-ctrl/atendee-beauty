using Atendee.Domain.Primitives;

namespace Atendee.Domain.Tenants;

public sealed class Tenant
{
    private const int MaxNameLength = 120;
    private const int MaxSlugLength = 60;
    private const int MaxTimeZoneIdLength = 128;

    private Tenant(
        TenantId id,
        string name,
        string slug,
        string timeZoneId,
        TenantStatus status,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Slug = slug;
        TimeZoneId = timeZoneId;
        Status = status;
        CreatedAtUtc = createdAtUtc;
    }

    public TenantId Id { get; }
    public string Name { get; private set; }
    public string Slug { get; }
    public string TimeZoneId { get; private set; }
    public TenantStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }

    public static Tenant Create(
        string name,
        string slug,
        string timeZoneId,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        var normalizedName = name.Trim();
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var normalizedTimeZoneId = timeZoneId.Trim();

        if (normalizedName.Length > MaxNameLength || normalizedName.Any(char.IsControl))
        {
            throw new ArgumentException("Tenant name is invalid or too long.", nameof(name));
        }

        if (normalizedSlug.Length is < 3 or > MaxSlugLength ||
            !char.IsAsciiLetterOrDigit(normalizedSlug[0]) ||
            !char.IsAsciiLetterOrDigit(normalizedSlug[^1]) ||
            normalizedSlug.Contains("--", StringComparison.Ordinal) ||
            normalizedSlug.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Slug must contain 3-60 lowercase letters/digits with single internal hyphens only.", nameof(slug));
        }

        if (normalizedTimeZoneId.Length > MaxTimeZoneIdLength)
        {
            throw new ArgumentException("Time zone ID is too long.", nameof(timeZoneId));
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalizedTimeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ArgumentException("A valid IANA or Windows time zone ID is required.", nameof(timeZoneId), exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ArgumentException("The specified time zone data is invalid.", nameof(timeZoneId), exception);
        }

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Creation timestamp must be UTC.", nameof(nowUtc));
        }

        return new Tenant(
            TenantId.New(),
            normalizedName,
            normalizedSlug,
            normalizedTimeZoneId,
            TenantStatus.PendingVerification,
            nowUtc);
    }

    // State transitions will be implemented in the tenant lifecycle use cases,
    // with persistence and authorization checks in the application layer.
}
