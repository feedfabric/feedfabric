using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FeedFabric.Application.Services.Storage;

public static class BlobStorageServiceCollectionExtensions
{
    // Registers IBlobStorage for the provider named in BlobStorage:Provider
    public static IServiceCollection AddBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(BlobStorageOptions.Section);
        services.Configure<BlobStorageOptions>(section);

        var provider = section.GetValue<string>(nameof(BlobStorageOptions.Provider)) ?? "Local";
        switch (provider.ToLowerInvariant())
        {
            case "local":
                services.AddSingleton<LocalBlobStorage>();
                services.AddSingleton<IBlobStorage>(sp => sp.GetRequiredService<LocalBlobStorage>());
                break;
            case "s3":
                services.AddSingleton<S3BlobStorage>();
                services.AddSingleton<IBlobStorage>(sp => sp.GetRequiredService<S3BlobStorage>());
                break;
            default:
                throw new InvalidOperationException($"Unknown BlobStorage:Provider '{provider}'. Use Local or S3.");
        }
        return services;
    }
}
