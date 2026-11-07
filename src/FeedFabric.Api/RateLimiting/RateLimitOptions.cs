namespace FeedFabric.Api.RateLimiting;

// How often one user can call each group of routes, counted in a fixed window. Requests without a user (signed out,
// or a failed token) share a limit per IP address. A request over its limit is a 429 rate_limited with Retry-After.
public class RateLimitOptions
{
    public const string Section = "RateLimits";

    // Turn off only when something in front of the API already limits per user
    public bool Enabled { get; set; } = true;

    public RateLimitRule Posting { get; set; } = new(10);

    public RateLimitRule Commenting { get; set; } = new(30);

    // Likes and reposts, and undoing them
    public RateLimitRule Reactions { get; set; } = new(120);

    public RateLimitRule Reports { get; set; } = new(10);

    // Starting and completing media uploads
    public RateLimitRule Uploads { get; set; } = new(20);

    // Impressions, opens, dwell, "not interested" and "show more": clients batch these, so the limit is generous
    public RateLimitRule Signals { get; set; } = new(600);

    // POST /ranking/preview, which ranks a whole candidate set
    public RateLimitRule RankingPreview { get; set; } = new(30);

    // GET /me/export and DELETE /me: an export reads everything the member has, so a few an hour
    public RateLimitRule Account { get; set; } = new(5) { Window = TimeSpan.FromHours(1) };
}

public class RateLimitRule
{
    public RateLimitRule()
    {
    }

    public RateLimitRule(int permitLimit) => PermitLimit = permitLimit;

    // Requests allowed per Window
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
