using System.Net;

namespace Observa.Connectors.Solana.Tests;

/// <summary>Returns a canned response for any request; captures the last request URI/body.</summary>
public sealed class StubHttpMessageHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public Uri? LastRequestUri { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequestUri = request.RequestUri;
        if (request.Content is not null) LastRequestBody = await request.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(status) { Content = new StringContent(body) };
    }
}
