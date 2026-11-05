using FeedFabric.Application.Services.Safety;

namespace FeedFabric.Application.Tests.Safety;

// Only the negative case is tested here: a repository is no place for test images of nudity. Check positives by hand
// with a moderation test set, outside the repo.
public class NudityClassifierTests
{
    [Fact]
    public async Task An_ordinary_photo_scores_low()
    {
        Models.SkipUnlessPresent("models/safety/nsfw");
        using var classifier = new NudityClassifier(Models.Storage());

        var score = await classifier.ScoreAsync(TestFiles.Jpeg(400, 300), TestContext.Current.CancellationToken);

        Assert.InRange(score, 0, 0.5);
    }
}
