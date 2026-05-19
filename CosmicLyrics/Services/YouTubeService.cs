using System.Text.RegularExpressions;

namespace CosmicLyrics.Services;

public class YouTubeService
{
    private static readonly Regex VideoIdRegex = new Regex(
        @"(?:youtube\.com\/watch\?v=|youtu\.be\/|youtube\.com\/embed\/)([a-zA-Z0-9_-]{11})",
        RegexOptions.Compiled);

    /// <summary>
    /// Extracts the video ID from a YouTube URL.
    /// </summary>
    public static string? ExtractVideoId(string url)
    {
        var match = VideoIdRegex.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Builds an embed URL for the YouTube player.
    /// enablejsapi=1 allows postMessage-based time sync.
    /// </summary>
    public static string BuildEmbedUrl(string videoId)
    {
        return $"https://www.youtube.com/embed/{videoId}" +
               "?enablejsapi=1" +
               "&autoplay=1" +
               "&rel=0" +
               "&modestbranding=1" +
               "&origin=https://cosmiclyrics.local";
    }

    /// <summary>
    /// Generates a YouTube search URL that opens in WebView2.
    /// </summary>
    public static string BuildSearchUrl(string query)
    {
        var encoded = Uri.EscapeDataString(query);
        return $"https://www.youtube.com/results?search_query={encoded}";
    }

    /// <summary>
    /// Parse artist/title from common YouTube title formats:
    /// "Artist - Title", "Title by Artist", "Artist: Title", etc.
    /// Returns (artist, title) or (null, fullTitle).
    /// </summary>
    public static (string? Artist, string Title) ParseVideoTitle(string youtubeTitle)
    {
        // Remove common suffixes
        var cleaned = Regex.Replace(youtubeTitle,
            @"\s*[\(\[][^)\]]*(?:official|lyrics?|audio|video|hd|mv|4k|ft\.?|feat\.?)[^)\]]*[\)\]]\s*",
            "", RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"\s*\|.*$", "").Trim();

        // "Artist - Title" (most common)
        var dashSplit = cleaned.Split(new[] { " - ", " – ", " — " }, 2, StringSplitOptions.None);
        if (dashSplit.Length == 2)
            return (dashSplit[0].Trim(), dashSplit[1].Trim());

        // No split found
        return (null, cleaned);
    }
}
