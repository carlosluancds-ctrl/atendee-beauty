using System.Text.Json;
using Atendee.Application.Abstractions;
using Atendee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Atendee.Infrastructure.Messaging;

/// <summary>
/// Configuration for the transactional outbox dispatcher.
/// </summary>
public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "OutboxDispatcher";

    /// <summary>
    /// RabbitMQ connection string (amqp://user:pass@host:5672/vhost).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Exchange name for outbox events.
    /// </summary>
    public string ExchangeName { get; set; } = "atendee.outbox";

    /// <summary>
    /// Exchange type (topic, fanout, direct).
    /// </summary>
    public string ExchangeType { get; set; } = "topic";

    /// <summary>
    /// Polling interval for checking the outbox table.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum number of messages to process per batch.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum retry attempts for a message before dead-lettering.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 5;

    /// <summary>
    /// Base delay for exponential backoff (will be multiplied by 2^attempt).
    /// </summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum delay between retries.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Dead letter exchange name.
    /// </summary>
    public string DeadLetterExchange { get; set; } = "atendee.dlx";

    /// <summary>
    /// Dead letter queue name.
    /// </summary>
    public string DeadLetterQueue { get; set; } = "atendee.outbox.dlq";

    /// <summary>
    /// Tenant database connection string (for polling outbox).
    /// </summary>
    public string TenantConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// Represents an outbox message ready for dispatch.
/// </summary>
internal sealed record OutboxMessage(
    Guid TenantId,
    Guid MessageId,
    Guid AggregateId,
    string EventType,
    string Payload,
    DateTimeOffset OccurredAtUtc,
    int Attempts);

/// <summary>
/// V101: Transactional outbox dispatcher with RabbitMQ.
/// Polls the transactional_outbox table, publishes messages to RabbitMQ with
/// exponential backoff retry, dead-letter queue, and idempotent processing.
/// </summary>
public sealed class OutboxDispatcher : BackgroundService
{
    private readonly ILogger<OutboxDispatcher> _logger;
    private readonly OutboxDispatcherOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private IConnection? _connection;
    private IChannel? _channel;

    public OutboxDispatcher(
        ILogger<OutboxDispatcher> logger,
        IOptions<OutboxDispatcherOptions> options,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox dispatcher starting...");

        // Initialize RabbitMQ connection
        await InitializeRabbitMqAsync(stoppingToken);

        // Declare exchanges and queues
        await SetupTopologyAsync(stoppingToken);

        _logger.LogInformation("Outbox dispatcher started. Polling every {PollInterval}", _options.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox batch");
            }

            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox dispatcher stopping...");
    }

    private async Task InitializeRabbitMqAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(_options.ConnectionString),
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            RequestedHeartbeat = TimeSpan.FromSeconds(30)
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        _connection.ConnectionShutdownAsync += async (sender, args) =>
        {
            _logger.LogWarning("RabbitMQ connection shutdown: {Reason}", args.ReplyText);
            await Task.CompletedTask;
        };

        _logger.LogInformation("RabbitMQ connection established");
    }

    private async Task SetupTopologyAsync(CancellationToken cancellationToken)
    {
        if (_channel is null) return;

        // Main exchange
        await _channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: _options.ExchangeType,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        // Dead letter exchange
        await _channel.ExchangeDeclareAsync(
            exchange: _options.DeadLetterExchange,
            type: "topic",
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        // Dead letter queue
        var dlqArgs = new Dictionary<string, object?>
        {
            { "x-message-ttl", 604800000 } // 7 days TTL for DLQ messages
        };
        await _channel.QueueDeclareAsync(
            queue: _options.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: dlqArgs,
            cancellationToken: cancellationToken);

        await _channel.QueueBindAsync(
            queue: _options.DeadLetterQueue,
            exchange: _options.DeadLetterExchange,
            routingKey: "#",
            cancellationToken: cancellationToken);

        _logger.LogInformation("RabbitMQ topology declared: exchange={Exchange}, dlx={DLX}, dlq={DLQ}",
            _options.ExchangeName, _options.DeadLetterExchange, _options.DeadLetterQueue);
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        // Get unprocessed messages that are available
        var messages = await GetPendingMessagesAsync(dbContext, cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Processing {Count} outbox messages", messages.Count);

        foreach (var message in messages)
        {
            if (cancellationToken.IsCancellationRequested) break;

            await ProcessMessageAsync(dbContext, message, cancellationToken);
        }
    }

    private async Task<List<OutboxMessage>> GetPendingMessagesAsync(
        OutboxDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT tenant_id, message_id, aggregate_id, event_type, payload,
                   occurred_at_utc, attempts
            FROM atendee.transactional_outbox
            WHERE processed_at_utc IS NULL
              AND available_at_utc <= NOW()
            ORDER BY occurred_at_utc, available_at_utc
            LIMIT {_options.BatchSize}
            FOR UPDATE SKIP LOCKED
            """;

        var messages = new List<OutboxMessage>();

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var occurredAt = reader.GetFieldValue<DateTime>(5);
            messages.Add(new OutboxMessage(
                TenantId: reader.GetGuid(0),
                MessageId: reader.GetGuid(1),
                AggregateId: reader.GetGuid(2),
                EventType: reader.GetString(3),
                Payload: reader.GetString(4),
                OccurredAtUtc: DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc),
                Attempts: reader.GetInt32(6)));
        }

        return messages;
    }

    private async Task ProcessMessageAsync(
        OutboxDbContext dbContext,
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        var routingKey = $"{message.TenantId:N}.{message.EventType}";

        try
        {
            if (_channel is null)
            {
                throw new InvalidOperationException("RabbitMQ channel not initialized");
            }

            var properties = new BasicProperties
            {
                Persistent = true,
                MessageId = message.MessageId.ToString("N"),
                CorrelationId = message.AggregateId.ToString("N"),
                Type = message.EventType,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    { "tenant_id", message.TenantId.ToString("N") },
                    { "aggregate_id", message.AggregateId.ToString("N") },
                    { "occurred_at_utc", message.OccurredAtUtc.ToString("O") },
                    { "attempts", message.Attempts }
                }
            };

            var body = System.Text.Encoding.UTF8.GetBytes(message.Payload);

            await _channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            // Mark as processed
            await MarkProcessedAsync(dbContext, message.MessageId, message.TenantId, cancellationToken);

            _logger.LogDebug("Published outbox message {MessageId} ({EventType}) for tenant {TenantId}",
                message.MessageId, message.EventType, message.TenantId);
        }
        catch (Exception ex)
        {
            await HandlePublishFailureAsync(dbContext, message, ex, cancellationToken);
        }
    }

    private async Task MarkProcessedAsync(
        OutboxDbContext dbContext,
        Guid messageId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var sql = """
            UPDATE atendee.transactional_outbox
            SET processed_at_utc = NOW()
            WHERE message_id = @message_id AND tenant_id = @tenant_id
            """;

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var param1 = command.CreateParameter();
        param1.ParameterName = "@message_id";
        param1.Value = messageId;
        command.Parameters.Add(param1);
        var param2 = command.CreateParameter();
        param2.ParameterName = "@tenant_id";
        param2.Value = tenantId;
        command.Parameters.Add(param2);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task HandlePublishFailureAsync(
        OutboxDbContext dbContext,
        OutboxMessage message,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var nextAttempt = message.Attempts + 1;

        if (nextAttempt >= _options.MaxRetryAttempts)
        {
            // Move to dead letter queue
            await MoveToDeadLetterAsync(dbContext, message, exception, cancellationToken);
            _logger.LogWarning("Message {MessageId} moved to DLQ after {Attempts} attempts: {Error}",
                message.MessageId, nextAttempt, exception.Message);
        }
        else
        {
            // Schedule retry with exponential backoff
            var delay = TimeSpan.FromSeconds(
                Math.Min(
                    _options.BaseRetryDelay.TotalSeconds * Math.Pow(2, nextAttempt - 1),
                    _options.MaxRetryDelay.TotalSeconds));

            var availableAt = DateTimeOffset.UtcNow.Add(delay);

            await ScheduleRetryAsync(dbContext, message, nextAttempt, availableAt, cancellationToken);

            _logger.LogWarning("Message {MessageId} scheduled for retry {Attempt}/{MaxAttempts} in {Delay}: {Error}",
                message.MessageId, nextAttempt, _options.MaxRetryAttempts, delay, exception.Message);
        }
    }

    private async Task ScheduleRetryAsync(
        OutboxDbContext dbContext,
        OutboxMessage message,
        int attempts,
        DateTimeOffset availableAt,
        CancellationToken cancellationToken)
    {
        var sql = """
            UPDATE atendee.transactional_outbox
            SET attempts = @attempts, available_at_utc = @available_at
            WHERE message_id = @message_id AND tenant_id = @tenant_id
            """;

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        var p1 = command.CreateParameter();
        p1.ParameterName = "@attempts";
        p1.Value = attempts;
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@available_at";
        p2.Value = availableAt.UtcDateTime;
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@message_id";
        p3.Value = message.MessageId;
        command.Parameters.Add(p3);

        var p4 = command.CreateParameter();
        p4.ParameterName = "@tenant_id";
        p4.Value = message.TenantId;
        command.Parameters.Add(p4);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MoveToDeadLetterAsync(
        OutboxDbContext dbContext,
        OutboxMessage message,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (_channel is null) return;

        try
        {
            var dlqProperties = new BasicProperties
            {
                Persistent = true,
                MessageId = message.MessageId.ToString("N"),
                CorrelationId = message.AggregateId.ToString("N"),
                Type = message.EventType,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    { "tenant_id", message.TenantId.ToString("N") },
                    { "aggregate_id", message.AggregateId.ToString("N") },
                    { "occurred_at_utc", message.OccurredAtUtc.ToString("O") },
                    { "attempts", message.Attempts },
                    { "error", exception.Message },
                    { "failed_at_utc", DateTimeOffset.UtcNow.ToString("O") }
                }
            };

            var body = System.Text.Encoding.UTF8.GetBytes(message.Payload);

            await _channel.BasicPublishAsync(
                exchange: _options.DeadLetterExchange,
                routingKey: $"dead.{message.TenantId:N}.{message.EventType}",
                mandatory: false,
                basicProperties: dlqProperties,
                body: body,
                cancellationToken: cancellationToken);

            // Mark as processed (moved to DLQ)
            await MarkProcessedAsync(dbContext, message.MessageId, message.TenantId, cancellationToken);
        }
        catch (Exception dlqEx)
        {
            _logger.LogError(dlqEx, "Failed to publish message {MessageId} to DLQ", message.MessageId);
            // Don't mark as processed - will retry on next poll
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Outbox dispatcher stopping...");

        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            _channel.Dispose();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync(cancellationToken);
            _connection.Dispose();
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("Outbox dispatcher stopped");
    }
}