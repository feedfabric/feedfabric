using Microsoft.Extensions.Configuration;

namespace FeedFabric.Application.Services;

// Settings a process can't run without, checked before it starts so a bad deploy fails at once with every problem
// listed, rather than at the first request that needs one. Development keeps its local defaults.
public static class StartupChecks
{
    public static void Validate(IConfiguration configuration, bool isDevelopment, params string[] requiredOutsideDevelopment)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Default")))
        {
            problems.Add("ConnectionStrings:Default (Postgres) is missing.");
        }

        if (!isDevelopment)
        {
            problems.AddRange(requiredOutsideDevelopment
                .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
                .Select(key => $"{key} is missing."));

            // Local storage is one machine's disk: uploads and models wouldn't be shared between instances
            var provider = configuration["BlobStorage:Provider"] ?? "Local";
            if (!provider.Equals("S3", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"BlobStorage:Provider must be S3 outside Development (it is \"{provider}\").");
            }
            else if (string.IsNullOrWhiteSpace(configuration["BlobStorage:S3:Bucket"]))
            {
                problems.Add("BlobStorage:S3:Bucket is missing.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "FeedFabric can't start: " + string.Join(" ", problems) + " See docs/configuration.md for each setting and its environment variable.");
        }
    }
}
