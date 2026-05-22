using System.Net;

namespace Observa.Connectors.Solana.Tests;

/// <summary>Routes each request to a canned response by matching the request URI + body against predicates.</summary>
public sealed class RoutingStubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(Func<string, string, bool> Match, HttpStatusCode Status, string Body)> _routes = new();

    public RoutingStubHttpMessageHandler Add(Func<string, string, bool> match, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add((match, status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri?.ToString() ?? "";
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        foreach (var (match, status, respBody) in _routes)
            if (match(uri, body))
                return new HttpResponseMessage(status) { Content = new StringContent(respBody) };
        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
    }
}
