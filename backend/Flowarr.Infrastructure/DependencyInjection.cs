using Flowarr.Core.Integrations;
using Flowarr.Core.Services;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Integrations;
using Flowarr.Infrastructure.Options;
using Flowarr.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flowarr.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFlowarrInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IntegrationOptions>(options => configuration.GetSection("Flowarr").Bind(options));

        var connectionString = configuration.GetConnectionString("Flowarr")
            ?? "Host=localhost;Port=5432;Database=flowarr;Username=flowarr;Password=flowarr";

        services.AddDbContext<FlowarrDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ICleanupRecommendationService, CleanupRecommendationService>();
        services.AddScoped<IDownloadClient, MockDownloadClient>();
        services.AddScoped<IMediaCatalogClient, MockRadarrClient>();
        services.AddScoped<LocalStorageScanner>();
        services.AddScoped<SeedboxStorageScanner>();
        services.AddScoped<IStorageScanner>(provider => provider.GetRequiredService<LocalStorageScanner>());
        services.AddScoped<IStorageScanner>(provider => provider.GetRequiredService<SeedboxStorageScanner>());
        services.AddHostedService<FlowarrSyncHostedService>();

        return services;
    }
}
