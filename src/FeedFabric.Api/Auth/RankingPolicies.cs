using Microsoft.AspNetCore.Authorization;

namespace FeedFabric.Api.Auth;

public static class RankingPolicies
{
    // Changing ranking profiles changes everyone's feed. Development lets any signed-in user in, for the simulator
    // app; everywhere else needs the "ranking-admin" role claim from the identity provider.
    public const string Admin = "RankingAdmin";
    public const string AdminRole = "ranking-admin";

    public static AuthorizationBuilder AddRankingPolicies(this AuthorizationBuilder builder, IHostEnvironment environment) =>
        builder.AddPolicy(Admin, policy =>
        {
            policy.RequireAuthenticatedUser();
            if (!environment.IsDevelopment())
            {
                policy.RequireRole(AdminRole);
            }
        });
}
