using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Observa.Connectors.Solana;

namespace Observa.Connectors.Solana.Tests;

public sealed class SolanaPollIntervalTests
{
    // Regression guard for the .NET TimeSpan parsing trap that made the Solana connector poll once
    // every 24 DAYS instead of hourly: "24:00:00" is parsed as days.hh:mm:ss when the first field is >= 24.
    [Fact]
    public void DotNet_Parses_24Colon00Colon00_As_24Days_NotHours()
    {
        TimeSpan.Parse("24:00:00").Should().Be(TimeSpan.FromDays(24));
        TimeSpan.Parse("24:00:00").Should().NotBe(TimeSpan.FromHours(24));
    }

    // The format we ship for an hourly cadence binds to exactly one hour through the standard
    // configuration binder (the same path the connectors use at startup).
    [Fact]
    public void ConfiguredHourlyInterval_BindsToOneHour()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Connectors:Solana:0:Id"] = "solana-main",
                ["Connectors:Solana:0:WalletAddress"] = "Wa11et",
                ["Connectors:Solana:0:PollInterval"] = "01:00:00",
            })
            .Build();

        var accounts = config.GetSection(SolanaOptions.SectionName).Get<SolanaOptions[]>();

        accounts.Should().ContainSingle();
        accounts![0].PollInterval.Should().Be(TimeSpan.FromHours(1));
        accounts[0].PollInterval.Should().BeLessThan(TimeSpan.FromDays(1),
            "a sane crypto poll cadence must never bind to days (catches the \"24:00:00\" trap)");
    }
}
