using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Blofin;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBlofinConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        var accounts = configuration.GetSection(BlofinOptions.SectionName).Get<BlofinOptions[]>() ?? [];

        services.AddHttpClient<BlofinAffiliateClient>(http =>
        {
            var baseUrl = accounts.FirstOrDefault()?.ApiBaseUrl ?? "https://openapi.blofin.com";
            http.BaseAddress = new Uri(baseUrl);
            http.Timeout = TimeSpan.FromSeconds(30);
        });

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var options in accounts)
        {
            if (string.IsNullOrWhiteSpace(options.Id))
                throw new InvalidOperationException("BloFin account configuration is missing required 'Id'.");

            if (!seenIds.Add(options.Id))
                throw new InvalidOperationException($"BloFin account id '{options.Id}' is duplicated in configuration.");

            services.AddSingleton<IConnector>(sp =>
                new BlofinConnector(options, sp.GetRequiredService<BlofinAffiliateClient>()));
        }

        return services;
    }
}
