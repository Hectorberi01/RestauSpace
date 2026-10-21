using System.Numerics;

namespace Orders.Domain;

/// <summary>
/// Répartit un montant selon des poids sans perdre d'unité mineure (méthode du plus fort reste sur entiers exacts).
/// Chaque part vaut floor(montant x poids / poids total) ; les unités mineures restantes vont une à une aux plus
/// forts restes. Les égalités vont au poids le plus fort, puis à la plus petite clé de départage, et seulement
/// ensuite à la première position : avec des clés tirées du contenu (le produit), le résultat ne dépend pas de
/// l'ordre dans lequel les poids sont donnés. La somme des parts égale toujours exactement le montant.
/// </summary>
internal static class ProportionalAllocator
{
    public static IReadOnlyList<Money> Allocate(Money amount, IReadOnlyList<Money> weights, IReadOnlyList<Guid> tieBreakKeys)
    {
        var currency = amount.Currency;
        var scale = 1m;
        for (var i = 0; i < currency.DecimalPlaces; i++)
        {
            scale *= 10m;
        }

        var total = ToMinorUnits(amount, scale);
        var weightUnits = weights.Select(weight => ToMinorUnits(weight, scale)).ToArray();
        var weightSum = weightUnits.Aggregate(BigInteger.Zero, (sum, weight) => sum + weight);

        var shares = new BigInteger[weightUnits.Length];
        if (!total.IsZero && !weightSum.IsZero)
        {
            var remainders = new BigInteger[weightUnits.Length];
            var allocated = BigInteger.Zero;
            for (var i = 0; i < weightUnits.Length; i++)
            {
                shares[i] = BigInteger.DivRem(total * weightUnits[i], weightSum, out remainders[i]);
                allocated += shares[i];
            }

            // Il reste strictement moins d'unités que de lignes ayant un reste non nul.
            var leftover = (int)(total - allocated);
            var byLargestRemainder = Enumerable.Range(0, shares.Length)
                .OrderByDescending(index => remainders[index])
                .ThenByDescending(index => weightUnits[index])
                .ThenBy(index => tieBreakKeys[index])
                .ThenBy(index => index)
                .Take(leftover);
            foreach (var index in byLargestRemainder)
            {
                shares[index] += BigInteger.One;
            }
        }

        return shares.Select(share => Money.Of((decimal)share / scale, currency)).ToArray();
    }

    private static BigInteger ToMinorUnits(Money money, decimal scale) => new(money.Amount * scale);
}
