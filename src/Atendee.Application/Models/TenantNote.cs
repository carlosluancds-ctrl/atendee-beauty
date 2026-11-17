namespace Atendee.Application.Models;

/// <summary>Non-sensitive sample record used to validate the tenant persistence boundary.</summary>
public sealed record TenantNote(Guid Id, string Body, DateTimeOffset CreatedAtUtc);
