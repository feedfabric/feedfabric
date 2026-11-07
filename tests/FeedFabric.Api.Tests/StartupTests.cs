using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FeedFabric.Api.Tests;

// Startup refuses configuration that can't work, before touching any dependency, so no containers are needed
public class StartupTests
{
    private sealed class Factory(string environment, Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }

    // Everything production needs, so each test can take one thing away
    private static Dictionary<string, string?> Production() => new()
    {
        ["ConnectionStrings:Default"] = "Host=db.internal;Database=feedfabric",
        ["ConnectionStrings:Redis"] = "redis.internal:6379",
        ["BlobStorage:Provider"] = "S3",
        ["BlobStorage:S3:Bucket"] = "feedfabric-media",
        ["Auth:Mode"] = "Jwt",
        ["Auth:Jwt:Audience"] = "feedfabric",
        ["Auth:Jwt:Issuer"] = "https://host.example",
        ["Auth:Jwt:SigningKey"] = "a-signing-key-of-at-least-thirty-two-bytes",
    };

    private static Exception StartFails(Dictionary<string, string?> settings, string environment = "Production")
    {
        using var factory = new Factory(environment, settings);
        var exception = Record.Exception(() => factory.CreateClient());
        Assert.NotNull(exception);
        return exception;
    }

    [Theory]
    [InlineData("ConnectionStrings:Default")]
    [InlineData("ConnectionStrings:Redis")]
    [InlineData("BlobStorage:S3:Bucket")]
    public void A_missing_setting_stops_startup(string key)
    {
        var settings = Production();
        settings[key] = "";

        Assert.Contains($"{key}", StartFails(settings).Message);
    }

    [Fact]
    public void Local_blob_storage_is_refused_outside_development()
    {
        var settings = Production();
        settings["BlobStorage:Provider"] = "Local";

        Assert.Contains("BlobStorage:Provider must be S3", StartFails(settings).Message);
    }

    [Fact]
    public void Empty_jwt_settings_stop_startup()
    {
        var settings = Production();
        settings["Auth:Jwt:Audience"] = "";
        settings["Auth:Jwt:SigningKey"] = "";

        Assert.Contains("Auth:Jwt", StartFails(settings).Message);
    }

    [Fact]
    public void A_topic_that_cant_be_a_tag_stops_startup()
    {
        var settings = Production();
        settings["Topics:Keywords:deep water:0"] = "dws";

        Assert.Contains("Topics:Keywords:deep water", StartFails(settings).Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Dev_auth_refuses_to_start_outside_development(string environment)
    {
        var settings = Production();
        settings["Auth:Mode"] = "Dev";

        Assert.Contains("only runs in Development", StartFails(settings, environment).Message);
    }
}
