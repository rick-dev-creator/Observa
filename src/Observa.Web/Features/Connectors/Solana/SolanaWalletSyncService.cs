using Microsoft.Extensions.Configuration;
using Observa.Connectors.Abstractions;
using Observa.Connectors.Solana;
using Observa.Features.Connectors.Domain;
using Observa.Features.Streams.Dtos;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Grains;
using Observa.Features.Streams.Services;

namespace Observa.Features.Connectors.Solana;

/// <summary>
/// Periodically scans each configured Solana wallet and auto-creates a Performance stream per token
/// worth >= MinValueUsd that is not already tracked. Tracking itself is handled by the snapshot poll.
/// </summary>
public sealed class SolanaWalletSyncService(
    IConfiguration configuration,
    IServiceProvider sp,
    IGrainFactory grains,
    SolanaWalletScanner scanner,
    ILogger<SolanaWalletSyncService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var accounts = configuration.GetSection(SolanaOptions.SectionName).Get<SolanaOptions[]>() ?? [];
        var active = accounts.Where(a => !string.IsNullOrWhiteSpace(a.WalletAddress)).ToArray();
        if (active.Length == 0) return;

        await Task.Delay(TimeSpan.FromSeconds(10), ct); // let the silo settle

        while (!ct.IsCancellationRequested)
        {
            foreach (var account in active)
            {
                try { await SyncWalletAsync(account, ct); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { logger.LogError(ex, "Solana wallet sync failed for {Id}.", account.Id); }
            }
            try { await Task.Delay(active.Min(a => a.SyncInterval), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task SyncWalletAsync(SolanaOptions account, CancellationToken ct)
    {
        var discovered = await scanner.ScanAsync(account.WalletAddress, account.MinValueUsd, ct);
        if (discovered.Count == 0) return;

        var existing = await ExistingMintsAsync(account.Id, ct);
        var toCreate = SolanaProvisioning.TokensToCreate(discovered, existing);
        if (toCreate.Count == 0) return;

        await using var scope = sp.CreateAsyncScope();
        var streams = scope.ServiceProvider.GetRequiredService<StreamService>();
        var created = 0;
        foreach (var token in toCreate)
        {
            var binding = ConnectorBinding.Create(new ConnectorId(account.Id), token.Mint, null, null);
            if (binding.IsFailure) continue;
            var dto = new RegisterStreamDto(token.Symbol, "Crypto", Direction.Performance, null, null, binding.Value);
            var result = await streams.RegisterAsync(dto, ct);
            if (result.IsSuccess) created++;
            else logger.LogWarning("Solana auto-provision failed for {Symbol}/{Mint}: {Errors}",
                token.Symbol, token.Mint, string.Join(",", result.Errors.Select(e => e.ErrorCode)));
        }
        logger.LogInformation("Solana wallet {Id}: discovered {Found}, created {Created} new stream(s).",
            account.Id, discovered.Count, created);
    }

    private async Task<HashSet<string>> ExistingMintsAsync(string connectorId, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var index = grains.GetGrain<IStreamIndexGrain>(StreamIndexGrain.SingletonKey);
        foreach (var id in await index.GetAllAsync())
        {
            var state = await grains.GetGrain<IStreamGrain>(id).GetAsync();
            if (state.Status is StreamStatus.Stopped or StreamStatus.Deleted) continue;
            if (state.Binding is { } b && string.Equals(b.ConnectorId, connectorId, StringComparison.OrdinalIgnoreCase))
                set.Add(b.ExternalRef);
        }
        return set;
    }
}
