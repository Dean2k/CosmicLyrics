using System.Net.Http;
using System.Web;
using CosmicLyrics.Models;
using Newtonsoft.Json;

namespace CosmicLyrics.Services;

public class LyricsService
{
    private readonly HttpClient _http;
    private const string BaseUrl = "https://lrclib.net/api";

    public LyricsService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "CosmicLyrics/1.0 (WPF App)");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<List<LyricLine>?> SearchLyricsAsync(string artistName, string trackName, int? duration = null)
    {
        try
        {
            var query = HttpUtility.UrlEncode($"{artistName} {trackName}");
            var url = $"{BaseUrl}/search?q={query}";

            var response = await _http.GetStringAsync(url);
            var results = JsonConvert.DeserializeObject<List<LrcLibTrack>>(response);

            if (results == null || results.Count == 0)
                return null;

            // Find best match with synced lyrics
            var best = results
                .Where(r => !string.IsNullOrEmpty(r.SyncedLyrics))
                .OrderBy(r => LevenshteinDistance(r.TrackName?.ToLower() ?? "", trackName.ToLower()))
                .FirstOrDefault();

            if (best?.SyncedLyrics == null)
            {
                // Fall back to plain lyrics displayed without timing
                best = results.FirstOrDefault(r => !string.IsNullOrEmpty(r.PlainLyrics));
                if (best?.PlainLyrics != null)
                    return ParsePlainLyrics(best.PlainLyrics);
                return null;
            }

            return LrcParser.Parse(best.SyncedLyrics);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lyrics fetch error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<LyricLine>?> GetByIdAsync(int id)
    {
        try
        {
            var url = $"{BaseUrl}/get/{id}";
            var response = await _http.GetStringAsync(url);
            var track = JsonConvert.DeserializeObject<LrcLibTrack>(response);

            if (track?.SyncedLyrics != null)
                return LrcParser.Parse(track.SyncedLyrics);
            if (track?.PlainLyrics != null)
                return ParsePlainLyrics(track.PlainLyrics);
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static List<LyricLine> ParsePlainLyrics(string plain)
    {
        var lines = new List<LyricLine>();
        var textLines = plain.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var t = TimeSpan.Zero;

        foreach (var line in textLines)
        {
            var text = line.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            lines.Add(new LyricLine
            {
                Timestamp = t,
                Text = text,
                Words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            });
            t += TimeSpan.FromSeconds(3);
        }

        // Set NextLineTimestamp for word sync
        for (int i = 0; i < lines.Count; i++)
            lines[i].NextLineTimestamp = i + 1 < lines.Count
                ? lines[i + 1].Timestamp
                : lines[i].Timestamp + TimeSpan.FromSeconds(4);

        return lines;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        int[,] dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                dp[i, j] = a[i - 1] == b[j - 1]
                    ? dp[i - 1, j - 1]
                    : 1 + Math.Min(dp[i - 1, j - 1], Math.Min(dp[i - 1, j], dp[i, j - 1]));
        return dp[a.Length, b.Length];
    }
}

public class LrcLibTrack
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("trackName")]
    public string? TrackName { get; set; }

    [JsonProperty("artistName")]
    public string? ArtistName { get; set; }

    [JsonProperty("albumName")]
    public string? AlbumName { get; set; }

    [JsonProperty("duration")]
    public double? Duration { get; set; }

    [JsonProperty("syncedLyrics")]
    public string? SyncedLyrics { get; set; }

    [JsonProperty("plainLyrics")]
    public string? PlainLyrics { get; set; }
}
