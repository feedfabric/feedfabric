using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FeedFabric.Application.Services.Tags;

public static class TopicServiceCollectionExtensions
{
    // Topics:Keywords is checked at startup, so a bad list fails the deploy rather than the first post
    public static IServiceCollection AddTopics(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TopicOptions>().Bind(configuration.GetSection(TopicOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TopicOptions>, TopicOptionsValidator>();
        services.AddSingleton<TopicRules>();
        services.AddScoped<TopicStore>();
        return services;
    }

    private sealed class TopicOptionsValidator : IValidateOptions<TopicOptions>
    {
        public ValidateOptionsResult Validate(string? name, TopicOptions options) =>
            options.Enabled && TopicMatcher.Problems(options.Keywords) is { Count: > 0 } problems
                ? ValidateOptionsResult.Fail(problems.Select(Describe))
                : ValidateOptionsResult.Success;

        private static string Describe(TopicError problem) =>
            problem.Topic == "" ? $"Topics:Keywords: {problem.Message}" : $"Topics:Keywords:{problem.Topic}: {problem.Message}";
    }
}
