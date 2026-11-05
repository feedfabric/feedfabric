using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace FeedFabric.Api.Auth;

public static class AuthServiceCollectionExtensions
{
    // "Dev" mode only in Development: any token or X-Dev-User is trusted. Anywhere else the host's tokens are checked:
    // against its OpenID Connect keys (Auth:Jwt:Authority) or its shared secret (Auth:Jwt:SigningKey), for
    // Auth:Jwt:Audience. Either way IdentityClaims maps the token's claims to FeedFabric's.
    public static IServiceCollection AddFeedFabricAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(AuthOptions.Section);
        services.Configure<AuthOptions>(section);
        var options = section.Get<AuthOptions>() ?? new AuthOptions();
        services.AddMemoryCache();
        services.AddTransient<IClaimsTransformation, IdentityClaims>();

        if (options.Mode.Equals(AuthModes.Dev, StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("Auth:Mode \"Dev\" trusts any token without checking it, so it only runs in Development.");
            }
            services.AddAuthentication(DevAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthenticationHandler.SchemeName, null);
            return services;
        }

        var jwt = options.Jwt;
        if (string.IsNullOrEmpty(jwt.Audience) || string.IsNullOrEmpty(jwt.Authority) == string.IsNullOrEmpty(jwt.SigningKey))
        {
            throw new InvalidOperationException(
                "Auth:Jwt needs an Audience, and either an Authority (an OpenID Connect issuer such as Auth0) or a SigningKey and Issuer.");
        }
        if (!string.IsNullOrEmpty(jwt.SigningKey) && (string.IsNullOrEmpty(jwt.Issuer) || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32))
        {
            throw new InvalidOperationException("Auth:Jwt:SigningKey needs an Issuer, and must be at least 32 bytes.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                // Keep claim names as the token has them, so AuthOptions.Claims matches what the provider documents
                bearer.MapInboundClaims = false;
                bearer.Audience = jwt.Audience;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidAudience = jwt.Audience,
                    ClockSkew = jwt.ClockSkew,
                    NameClaimType = options.Claims.UserId,
                };
                // A valid token that doesn't say who the user is can't be used: refuse it here (401), not later
                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        // Read the same way IdentityClaims reads it, so a dotted path into a JSON claim works too
                        if (context.Principal is null || !IdentityClaims.Values(context.Principal, options.Claims.UserId).Any(v => v.Length > 0))
                        {
                            context.Fail($"The token has no \"{options.Claims.UserId}\" claim (Auth:Claims:UserId).");
                        }
                        return Task.CompletedTask;
                    },
                };
                if (!string.IsNullOrEmpty(jwt.Authority))
                {
                    bearer.Authority = jwt.Authority;
                    bearer.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
                    if (!string.IsNullOrEmpty(jwt.Issuer))
                    {
                        bearer.TokenValidationParameters.ValidIssuer = jwt.Issuer;
                    }
                }
                else
                {
                    bearer.TokenValidationParameters.ValidIssuer = jwt.Issuer;
                    bearer.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey!));
                    bearer.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256, SecurityAlgorithms.HmacSha384, SecurityAlgorithms.HmacSha512];
                }
            });
        return services;
    }
}
