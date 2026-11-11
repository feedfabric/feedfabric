using Microsoft.AspNetCore.Authorization;

namespace FeedFabric.Api.Auth;

public static class AdminPolicies
{
    // Anyone who runs the service: moderators, ranking admins and integration admins. The admin panel's overview and system pages.
    // Development lets any signed-in user in, as the other admin policies do.
    public const string Staff = "Staff";

    // How the host's own servers plug in: API keys and post data types. A key can act for any member, so this needs
    // its own role (AuthOptions.Roles.IntegrationAdmin) rather than any staff one.
    public const string IntegrationAdmin = "IntegrationAdmin";
    public const string IntegrationAdminRole = "integration-admin";

    public static AuthorizationBuilder AddAdminPolicies(this AuthorizationBuilder builder, IHostEnvironment environment) =>
        builder
            .AddPolicy(Staff, policy =>
            {
                policy.RequireAuthenticatedUser();
                // API keys act for members, never staff, in Development too
                policy.RequireAssertion(context => !IdentityClaims.IsApiKey(context.User));
                if (!environment.IsDevelopment())
                {
                    policy.RequireClaim(IdentityClaims.Role, ModerationPolicies.ReviewerRole, RankingPolicies.AdminRole, IntegrationAdminRole);
                }
            })
            .AddPolicy(IntegrationAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => !IdentityClaims.IsApiKey(context.User));
                if (!environment.IsDevelopment())
                {
                    policy.RequireClaim(IdentityClaims.Role, IntegrationAdminRole);
                }
            });
}
