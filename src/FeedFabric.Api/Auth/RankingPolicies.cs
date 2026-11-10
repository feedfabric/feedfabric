using Microsoft.AspNetCore.Authorization;

namespace FeedFabric.Api.Auth;

public static class RankingPolicies
{
    // Changing ranking profiles changes everyone's feed. Development lets any signed-in user in, for the simulator
    // app; everywhere else needs a role the host's identity provider grants that AuthOptions.Roles.RankingAdmin lists.
    public const string Admin = "RankingAdmin";
    public const string AdminRole = "ranking-admin";

    public static AuthorizationBuilder AddRankingPolicies(this AuthorizationBuilder builder, IHostEnvironment environment) =>
        builder.AddPolicy(Admin, policy =>
        {
            policy.RequireAuthenticatedUser();
            // API keys act for members, never staff, in Development too
            policy.RequireAssertion(context => !IdentityClaims.IsApiKey(context.User));
            if (!environment.IsDevelopment())
            {
                // Mapped from the host's own role names by IdentityClaims (AuthOptions.Roles)
                policy.RequireClaim(IdentityClaims.Role, AdminRole);
            }
        });
}
