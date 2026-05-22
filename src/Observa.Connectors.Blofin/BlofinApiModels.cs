using System.Text.Json.Serialization;

namespace Observa.Connectors.Blofin;

internal sealed record BlofinApiResponse<T>
{
    [JsonPropertyName("code")] public string Code { get; init; } = "";
    [JsonPropertyName("msg")] public string Msg { get; init; } = "";
    [JsonPropertyName("data")] public T? Data { get; init; }
}

internal sealed record InviteeDto
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("uid")] public string Uid { get; init; } = "";

    /// <summary>Unix ms when this invitee registered — the earliest date they could have generated commission.</summary>
    [JsonPropertyName("registerTime")] public string RegisterTime { get; init; } = "";
}

internal sealed record DailyCommissionDto
{
    [JsonPropertyName("uid")] public string Uid { get; init; } = "";
    [JsonPropertyName("commission")] public string Commission { get; init; } = "";
    [JsonPropertyName("commissionTime")] public string CommissionTime { get; init; } = "";
    [JsonPropertyName("cashback")] public string Cashback { get; init; } = "";
    [JsonPropertyName("fee")] public string Fee { get; init; } = "";
    [JsonPropertyName("tradingVolume")] public string TradingVolume { get; init; } = "";
}
