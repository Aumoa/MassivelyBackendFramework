namespace NoraeBook.Services;

public static class KaraokeSongTags
{
    public const string Male = "male";

    public const string Female = "female";

    public const string Mixed = "mixed";

    public static readonly IReadOnlyList<string> DefaultTagIds = [Male, Female];

    public static IReadOnlyList<string> NormalizeForStorage(IEnumerable<string> tags)
    {
        var normalizedTags = new List<string>();
        foreach (var tag in tags)
        {
            var trimmedTag = tag.Trim();
            if (string.IsNullOrWhiteSpace(trimmedTag))
            {
                continue;
            }

            if (IsMixedTag(trimmedTag))
            {
                normalizedTags.Add(Male);
                normalizedTags.Add(Female);
                continue;
            }

            normalizedTags.Add(trimmedTag);
        }

        return normalizedTags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsDefaultTag(string tag)
    {
        return DefaultTagIds.Contains(tag, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsMixedTag(string tag)
    {
        return string.Equals(tag, Mixed, StringComparison.OrdinalIgnoreCase);
    }
}
