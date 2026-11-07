using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FeedFabric.Api.Tests;

// Keeps what the "FeedFabric.Audit" category logs
public sealed class AuditFactory : ApiFactory
{
    public ConcurrentQueue<string> Audit { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddLogging(logging => logging.AddProvider(new AuditProvider(Audit))));
    }

    private sealed class AuditProvider(ConcurrentQueue<string> audit) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new AuditLogger(categoryName == "FeedFabric.Audit" ? audit : null);
        public void Dispose() { }
    }

    private sealed class AuditLogger(ConcurrentQueue<string>? audit) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => audit is not null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            audit?.Enqueue(formatter(state, exception));
    }
}

public class SecurityTests(AuditFactory api) : IClassFixture<AuditFactory>
{
    [Fact]
    public async Task Responses_carry_the_security_headers_and_no_server_header()
    {
        var response = await api.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Content.Headers.Contains("Content-Security-Policy")
            ? response.Content.Headers.GetValues("Content-Security-Policy").Single()
            : response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Staff_changes_are_audited_with_who_and_the_outcome()
    {
        var client = api.ClientFor("staff-auditor", "moderator");

        var response = await client.PutAsync("/admin/users/no-such-user/verified",
            new StringContent("""{"verified":true}""", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        await client.GetAsync("/admin/users", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var entry = Assert.Single(api.Audit);
        Assert.Contains("PUT /admin/users/no-such-user/verified by staff-auditor", entry);
        Assert.EndsWith("404", entry);
    }
}
