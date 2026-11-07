using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FeedFabric.Application;

// For dotnet ef and the migration bundle, so they don't start the API to get a context. The connection string comes
// from ConnectionStrings__Default (the Makefile exports it from .env) or the bundle's --connection.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=localhost;Database=feedfabric")
            .Options);
}
