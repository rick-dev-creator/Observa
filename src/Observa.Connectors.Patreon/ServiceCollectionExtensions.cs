using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Observa.Connectors.Abstractions;

namespace Observa.Connectors.Patreon;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPatreonConnector(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PatreonOptions>(configuration.GetSection(PatreonOptions.SectionName));
        services.AddHttpClient<PatreonApiClient>();
        services.AddSingleton<IConnector, PatreonConnector>();
        return services;
    }
}
