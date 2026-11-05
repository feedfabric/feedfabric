using System.Text.RegularExpressions;

namespace FeedFabric.Application.Services.Storage;

public static partial class BlobKey
{
    // No "..", no leading '/', no backslashes: a key can never point outside its bucket or folder
    [GeneratedRegex(@"^(?!.*\.\.)(?!/)[A-Za-z0-9][A-Za-z0-9/_.-]{0,511}$")]
    private static partial Regex ValidPattern();

    public static string Validate(string key) =>
        ValidPattern().IsMatch(key) ? key : throw new ArgumentException($"Invalid blob key '{key}'.", nameof(key));
}
