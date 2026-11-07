using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FeedFabric.Application.Services.Tags;

public enum TopicSource
{
    // Topics:Enabled is false: hashtags only
    Off,
    // Chosen by staff in the admin panel
    Saved,
    // Topics:Keywords, until staff choose
    Configuration,
    // The built-in wellness set, until anyone chooses
    Default,
}

// The topics posts are tagged with now. Version is the saved set's, 0 when it isn't from the database.
public sealed record CurrentTopics(TopicSource Source, int Version, string? Library, IReadOnlyDictionary<string, List<string>> Topics,
    DateTime? SavedAt, string? SavedBy, TopicMatcher Matcher);

// Which topics apply: what staff saved in the admin panel, else Topics:Keywords, else the built-in wellness set.
// Topics:Enabled false turns them off whatever was saved. Checks for a newly saved set at most once a minute, so
// every instance picks up a change within a minute, and the one that saved it straight away (Changed).
public sealed class TopicRules(IServiceScopeFactory scopes, IOptions<TopicOptions> options, TimeProvider timeProvider)
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim gate = new(1, 1);
    private CurrentTopics? current;
    private DateTimeOffset nextCheck = DateTimeOffset.MinValue;

    public async Task<CurrentTopics> CurrentAsync(CancellationToken cancellationToken)
    {
        if (current is { } known && timeProvider.GetUtcNow() < nextCheck)
        {
            return known;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have loaded it while this one waited
            if (current is { } loaded && timeProvider.GetUtcNow() < nextCheck)
            {
                return loaded;
            }

            current = await LoadAsync(current, cancellationToken);
            nextCheck = timeProvider.GetUtcNow().Add(CheckEvery);
            return current;
        }
        finally
        {
            gate.Release();
        }
    }

    // A new set was saved here: read it on the next post rather than within the minute
    public void Changed() => nextCheck = DateTimeOffset.MinValue;

    private async Task<CurrentTopics> LoadAsync(CurrentTopics? previous, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return previous is { Source: TopicSource.Off } ? previous : Compile(TopicSource.Off, 0, null, [], null, null);
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var version = await db.TopicSets.MaxAsync(t => (int?)t.Version, cancellationToken);
        if (version is not null)
        {
            if (previous is { Source: TopicSource.Saved } && previous.Version == version)
            {
                return previous;
            }
            var row = await db.TopicSets.AsNoTracking().SingleAsync(t => t.Version == version, cancellationToken);
            return Compile(TopicSource.Saved, row.Version, row.Library, TopicStore.Deserialize(row.Topics), row.SavedAt, row.SavedBy);
        }

        // Nothing saved: these don't change while the process runs, so keep what's compiled
        var source = settings.Keywords.Count > 0 ? TopicSource.Configuration : TopicSource.Default;
        if (previous?.Source == source)
        {
            return previous;
        }
        return source == TopicSource.Configuration
            ? Compile(source, 0, null, TopicMatcher.Normalise(settings.Keywords), null, null)
            : Compile(source, 0, TopicLibrary.Default.Key, TopicMatcher.From(TopicLibrary.Default), null, null);
    }

    private static CurrentTopics Compile(TopicSource source, int version, string? library, Dictionary<string, List<string>> topics,
        DateTime? savedAt, string? savedBy) =>
        new(source, version, library, topics, savedAt, savedBy, new TopicMatcher(topics));
}
