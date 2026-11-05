using Microsoft.AspNetCore.Authorization;

namespace FeedFabric.Api.Auth;

public static class ModerationPolicies
{
    // Reviewing held posts and comments shows content nobody else can see and decides what members see. Development
    // lets any signed-in user in, for the admin app; everywhere else needs a role the host's identity provider
    // grants that AuthOptions.Roles.Moderator lists.
    public const string Reviewer = "ModerationReviewer";
    public const string ReviewerRole = "moderator";

    public static AuthorizationBuilder AddModerationPolicies(this AuthorizationBuilder builder, IHostEnvironment environment) =>
        builder.AddPolicy(Reviewer, policy =>
        {
            policy.RequireAuthenticatedUser();
            if (!environment.IsDevelopment())
            {
                // Mapped from the host's own role names by IdentityClaims (AuthOptions.Roles)
                policy.RequireClaim(IdentityClaims.Role, ReviewerRole);
            }
        });
}
