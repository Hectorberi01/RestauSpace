namespace SaasRestaurant.BuildingBlocks.Domain;

/// <summary>Devise ISO 4217 avec son nombre de décimales (unité mineure).</summary>
public sealed class Currency : ValueObject
{
    private const string TwoDecimalCodes =
        "AED AFN ALL AMD ANG AOA ARS AUD AWG AZN BAM BBD BDT BGN BMD BND BOB BRL BSD BTN BWP BYN BZD CAD CDF CHF " +
        "CNY COP CRC CUP CVE CZK DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GHS GIP GMD GTQ GYD HKD HNL HTG HUF " +
        "IDR ILS INR IRR JMD KES KGS KHR KPW KYD KZT LAK LBP LKR LRD LSL MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR " +
        "MWK MXN MYR MZN NAD NGN NIO NOK NPR NZD PAB PEN PGK PHP PKR PLN QAR RON RSD RUB SAR SBD SCR SDG SEK SGD " +
        "SHP SLE SOS SRD SSP STN SVC SYP SZL THB TJS TMT TOP TRY TTD TWD TZS UAH USD UYU UZS VED VES WST XCD XCG YER ZAR ZMW ZWG";

    private const string ZeroDecimalCodes = "BIF CLP DJF GNF ISK JPY KMF KRW PYG RWF UGX VND VUV XAF XOF XPF";

    private const string ThreeDecimalCodes = "BHD IQD JOD KWD LYD OMR TND";

    private static readonly Dictionary<string, Currency> Known = BuildKnown();

    private Currency(string code, int decimalPlaces)
    {
        Code = code;
        DecimalPlaces = decimalPlaces;
    }

    public string Code { get; }

    public int DecimalPlaces { get; }

    public static Currency EUR => Known["EUR"];

    public static Currency USD => Known["USD"];

    public static Currency XOF => Known["XOF"];

    public static Currency FromCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        DomainException.ThrowIf(
            normalized is null || !Known.ContainsKey(normalized),
            CommonErrors.CurrencyUnknown);

        return Known[normalized!];
    }

    public static bool IsKnown(string? code) =>
        code is not null && Known.ContainsKey(code.Trim().ToUpperInvariant());

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Code;
    }

    public override string ToString() => Code;

    private static Dictionary<string, Currency> BuildKnown()
    {
        var known = new Dictionary<string, Currency>(StringComparer.Ordinal);
        Add(TwoDecimalCodes, 2);
        Add(ZeroDecimalCodes, 0);
        Add(ThreeDecimalCodes, 3);
        return known;

        void Add(string codes, int decimalPlaces)
        {
            foreach (var code in codes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                known.Add(code, new Currency(code, decimalPlaces));
            }
        }
    }
}
