namespace Atendee.Application.Models;

public sealed record CustomerModel(
    Guid Id,
    string FullName,
    string? NormalizedPhoneNumber,
    string? EmailAddress,
    bool IsArchived,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
