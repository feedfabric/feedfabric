using Microsoft.AspNetCore.Authorization;

namespace FeedFabric.Api.Auth;

public static class AdminPolicies
{
    // Anyone who runs the service: moderators and ranking admins. The admin panel's overview and system pages.
    // Development lets any signed-in user in, as the other admin policies do.
    public const string Staff = "Staff";

    public static AuthorizationBuilder AddAdminPolicies(this AuthorizationBuilder builder, IHostEnvironment environment) =>
        builder.AddPolicy(Staff, policy =>
        {
            policy.RequireAuthenticatedUser();
            if (!environment.IsDevelopment())
            {
                policy.RequireClaim(IdentityClaims.Role, ModerationPolicies.ReviewerRole, RankingPolicies.AdminRole);
            }
        });
}
