using System.Net.Mail;

namespace Atendee.Domain.Customers;

/// <summary>
/// Minimal tenant-owned customer contact record. Free-form notes and marketing
/// consent are intentionally modeled separately to keep collection purpose-bound.
/// </summary>
public sealed record Customer(
    Guid Id,
    string FullName,
    string? NormalizedPhoneNumber,
    string? EmailAddress,
    bool IsArchived,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static Customer Create(
        Guid id,
        string fullName,
        string? phoneNumber,
        string? emailAddress,
        DateTimeOffset nowUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Customer ID is required.", nameof(id));

        ValidateUtc(nowUtc, nameof(nowUtc));

        var normalizedName = NormalizeName(fullName);
        var normalizedPhone = NormalizePhone(phoneNumber);
        var normalizedEmail = NormalizeEmail(emailAddress);
        if (normalizedPhone is null && normalizedEmail is null)
            throw new ArgumentException("At least one valid customer contact method is required.");

        return new Customer(
            id, normalizedName, normalizedPhone, normalizedEmail, false, nowUtc, nowUtc);
    }

    public Customer Update(
        string fullName,
        string? phoneNumber,
        string? emailAddress,
        DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        if (IsArchived)
            throw new InvalidOperationException("Archived customers cannot be updated.");
        if (nowUtc < CreatedAtUtc)
            throw new ArgumentException("Update timestamp cannot precede creation.", nameof(nowUtc));

        var normalizedPhone = NormalizePhone(phoneNumber);
        var normalizedEmail = NormalizeEmail(emailAddress);
        if (normalizedPhone is null && normalizedEmail is null)
            throw new ArgumentException("At least one valid customer contact method is required.");

        return this with
        {
            FullName = NormalizeName(fullName),
            NormalizedPhoneNumber = normalizedPhone,
            EmailAddress = normalizedEmail,
            UpdatedAtUtc = nowUtc
        };
    }

    public Customer Archive(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        if (IsArchived)
            return this;
        if (nowUtc < CreatedAtUtc)
            throw new ArgumentException("Archive timestamp cannot precede creation.", nameof(nowUtc));

        return this with { IsArchived = true, UpdatedAtUtc = nowUtc };
    }

    private static string NormalizeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 120 || normalized.Any(char.IsControl))
            throw new ArgumentException("Customer name is invalid or exceeds 120 characters.", nameof(value));

        return normalized;
    }

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var compact = new string(value.Trim()
            .Where(character => character is not (' ' or '-' or '(' or ')' or '.'))
            .ToArray());

        if (compact.Length is < 9 or > 16 ||
            compact[0] != '+' ||
            compact[1] is < '1' or > '9' ||
            compact.Skip(1).Any(character => !char.IsAsciiDigit(character)))
        {
            throw new ArgumentException("Phone number must be a valid E.164 number.", nameof(value));
        }

        return compact;
    }

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 254 || normalized.Any(char.IsControl))
            throw new ArgumentException("Customer email address is invalid.", nameof(value));

        try
        {
            var parsed = new MailAddress(normalized);
            if (!string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Customer email address is invalid.", nameof(value));
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Customer email address is invalid.", nameof(value), exception);
        }

        return normalized;
    }

    private static void ValidateUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }
}
