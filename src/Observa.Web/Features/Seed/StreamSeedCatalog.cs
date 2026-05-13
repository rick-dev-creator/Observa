using Observa.Features.Streams.Enums;

namespace Observa.Features.Seed;

internal static class StreamSeedCatalog
{
    public static IReadOnlyList<SeedItem> Build() =>
    [
        // 10 income streams — mix fixed + variable
        new("Salary - Bank Job",      "Work",        Direction.Income,  8500m, AnchorDay: 1,  Variability.Fixed),
        new("Patreon Main",           "Content",     Direction.Income,   850m, AnchorDay: 5,  Variability.Variable),
        new("Patreon Art",            "Content",     Direction.Income,   320m, AnchorDay: 5,  Variability.Variable),
        new("Patreon Music",          "Content",     Direction.Income,   180m, AnchorDay: 5,  Variability.Variable),
        new("Blofin Trading Payout",  "Crypto",      Direction.Income,  1200m, AnchorDay: 15, Variability.Variable),
        new("Freelance Client A",     "Consulting",  Direction.Income,  3000m, AnchorDay: 10, Variability.Variable),
        new("Freelance Client B",     "Consulting",  Direction.Income,  1500m, AnchorDay: 20, Variability.Variable),
        new("Stock Dividends",        "Investments", Direction.Income,   450m, AnchorDay: 25, Variability.Variable),
        new("Rental Income",          "Real Estate", Direction.Income,  2200m, AnchorDay: 1,  Variability.Fixed),
        new("YouTube AdSense",        "Content",     Direction.Income,   280m, AnchorDay: 22, Variability.Variable),

        // 5 outcome streams
        new("Rent",                   "Housing",     Direction.Outcome, 2400m, AnchorDay: 1,  Variability.Fixed),
        new("Subscriptions",          "Software",    Direction.Outcome,  145m, AnchorDay: 5,  Variability.Variable),
        new("Utilities",              "Housing",     Direction.Outcome,  350m, AnchorDay: 10, Variability.Variable),
        new("Health Insurance",       "Health",      Direction.Outcome,  480m, AnchorDay: 15, Variability.Fixed),
        new("Groceries Budget",       "Food",        Direction.Outcome,  800m, AnchorDay: 1,  Variability.Variable),
    ];

    public sealed record SeedItem(
        string Name,
        string Category,
        Direction Direction,
        decimal ExpectedAmount,
        int AnchorDay,
        Variability Variability);
}
