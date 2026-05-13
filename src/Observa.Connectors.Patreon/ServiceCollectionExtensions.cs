using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Patreon;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPatreonConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<PatreonApiClient>();

        var accounts = configuration.GetSection(PatreonOptions.SectionName).Get<PatreonOptions[]>() ?? [];
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var options in accounts)
        {
            if (string.IsNullOrWhiteSpace(options.Id))
                throw new InvalidOperationException("Patreon account configuration is missing required 'Id'.");

            if (!seenIds.Add(options.Id))
                throw new InvalidOperationException($"Patreon account id '{options.Id}' is duplicated in configuration.");

            services.AddSingleton<IConnector>(sp =>
                new PatreonConnector(options, sp.GetRequiredService<PatreonApiClient>()));
        }

        return services;
    }
}
