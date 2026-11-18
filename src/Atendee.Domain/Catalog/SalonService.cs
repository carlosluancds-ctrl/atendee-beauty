using System.Globalization;

namespace Atendee.Domain.Catalog;

/// <summary>
/// A salon-owned service definition. Money is represented in the currency's
/// minor unit (for example, cents) and never as a floating-point value.
/// </summary>
public sealed record SalonService(
    Guid Id,
    string Name,
    int DurationMinutes,
    long PriceMinorUnits,
    string Currency,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    private static readonly HashSet<string> IsoCurrencyCodes = BuildIsoCurrencyCodes();

    public static SalonService Create(
        Guid id,
        string name,
        int durationMinutes,
        long priceMinorUnits,
        string currency,
        int bufferBeforeMinutes,
        int bufferAfterMinutes,
        bool isActive,
        DateTimeOffset nowUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Service ID is required.", nameof(id));
        var normalizedName = NormalizeName(name);
        ValidateCommercialFields(durationMinutes, priceMinorUnits, currency,
            bufferBeforeMinutes, bufferAfterMinutes);
        ValidateUtc(nowUtc, nameof(nowUtc));

        return new SalonService(
            id,
            normalizedName,
            durationMinutes,
            priceMinorUnits,
            currency.Trim().ToUpperInvariant(),
            bufferBeforeMinutes,
            bufferAfterMinutes,
            isActive,
            nowUtc,
            nowUtc);
    }

    public SalonService Update(
        string name,
        int durationMinutes,
        long priceMinorUnits,
        string currency,
        int bufferBeforeMinutes,
        int bufferAfterMinutes,
        bool isActive,
        DateTimeOffset nowUtc)
    {
        var normalizedName = NormalizeName(name);
        ValidateCommercialFields(durationMinutes, priceMinorUnits, currency,
            bufferBeforeMinutes, bufferAfterMinutes);
        ValidateUtc(nowUtc, nameof(nowUtc));
        if (nowUtc < CreatedAtUtc)
            throw new ArgumentException("Update timestamp cannot precede creation.", nameof(nowUtc));

        return this with
        {
            Name = normalizedName,
            DurationMinutes = durationMinutes,
            PriceMinorUnits = priceMinorUnits,
            Currency = currency.Trim().ToUpperInvariant(),
            BufferBeforeMinutes = bufferBeforeMinutes,
            BufferAfterMinutes = bufferAfterMinutes,
            IsActive = isActive,
            UpdatedAtUtc = nowUtc
        };
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > 120 || normalized.Any(char.IsControl))
            throw new ArgumentException("Service name is invalid or exceeds 120 characters.", nameof(name));
        return normalized;
    }

    private static void ValidateCommercialFields(
        int durationMinutes,
        long priceMinorUnits,
        string currency,
        int bufferBeforeMinutes,
        int bufferAfterMinutes)
    {
        if (durationMinutes is < 1 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(durationMinutes), "Duration must be between 1 and 1440 minutes.");
        if (priceMinorUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(priceMinorUnits), "Price cannot be negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 ||
            !IsoCurrencyCodes.Contains(normalizedCurrency))
            throw new ArgumentException("Currency must be a valid ISO 4217 code.", nameof(currency));
        if (bufferBeforeMinutes is < 0 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(bufferBeforeMinutes), "Pre-service buffer must be between 0 and 1440 minutes.");
        if (bufferAfterMinutes is < 0 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(bufferAfterMinutes), "Post-service buffer must be between 0 and 1440 minutes.");
    }

    private static HashSet<string> BuildIsoCurrencyCodes()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var code = new RegionInfo(culture.Name).ISOCurrencySymbol;
                if (code.Length == 3 && code.All(char.IsAsciiLetter))
                    codes.Add(code.ToUpperInvariant());
            }
            catch (ArgumentException)
            {
                // Some specific cultures do not map to a region.
            }
        }

        return codes;
    }

    private static void ValidateUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }
}
