using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CosmicLyrics.Models;

public class LyricLine : INotifyPropertyChanged
{
    private bool _isActive;

    public TimeSpan Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;
    public string RomanizedText { get; set; } = string.Empty;
    public string[] Words { get; set; } = Array.Empty<string>();
    public TimeSpan NextLineTimestamp { get; set; }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class LrcParser
{
    private static readonly Regex TimestampRegex = new Regex(
        @"\[(\d{2}):(\d{2})\.(\d{2,3})\](.*)$",
        RegexOptions.Compiled);

    public static List<LyricLine> Parse(string lrcContent)
    {
        var lines = new List<LyricLine>();

        foreach (var rawLine in lrcContent.Split('\n'))
        {
            var trimmed = rawLine.Trim();
            var match = TimestampRegex.Match(trimmed);
            if (!match.Success) continue;

            var minutes = int.Parse(match.Groups[1].Value);
            var seconds = int.Parse(match.Groups[2].Value);
            var msStr   = match.Groups[3].Value;
            var ms      = int.Parse(msStr.PadRight(3, '0').Substring(0, 3));
            var text    = match.Groups[4].Value.Trim();

            if (string.IsNullOrEmpty(text)) continue;

            lines.Add(new LyricLine
            {
                Timestamp = new TimeSpan(0, 0, minutes, seconds, ms),
                Text = text,
                Words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            });
        }

        lines = lines.OrderBy(l => l.Timestamp).ToList();

        for (int i = 0; i < lines.Count; i++)
            lines[i].NextLineTimestamp = i + 1 < lines.Count
                ? lines[i + 1].Timestamp
                : lines[i].Timestamp + TimeSpan.FromSeconds(4);

        return lines;
    }
}
