using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using CosmicLyrics.Models;
using CosmicLyrics.Services;

namespace CosmicLyrics.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly LyricsService _lyricsService;

    private List<LyricLine> _allLyrics = new();
    private int _activeLine = -1;
    private string _searchText = string.Empty;
    private string _embedUrl = string.Empty;
    private string _statusText = "Enter a YouTube URL or search for a song";
    private string _artistInput = string.Empty;
    private string _trackInput = string.Empty;
    private bool _isLoadingLyrics;
    private bool _isSynced;
    private double _currentTime;
    private string _videoTitle = string.Empty;
    private bool _autoScrollEnabled = true;
    private LyricLine? _centerLine;

    public ObservableCollection<LyricLine> DisplayedLyrics { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); }
    }

    public string EmbedUrl
    {
        get => _embedUrl;
        set { _embedUrl = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public string ArtistInput
    {
        get => _artistInput;
        set { _artistInput = value; OnPropertyChanged(); }
    }

    public string TrackInput
    {
        get => _trackInput;
        set { _trackInput = value; OnPropertyChanged(); }
    }

    public bool IsLoadingLyrics
    {
        get => _isLoadingLyrics;
        set { _isLoadingLyrics = value; OnPropertyChanged(); }
    }

    public bool IsSynced
    {
        get => _isSynced;
        set { _isSynced = value; OnPropertyChanged(); }
    }

    public string VideoTitle
    {
        get => _videoTitle;
        set { _videoTitle = value; OnPropertyChanged(); }
    }

    public bool AutoScrollEnabled
    {
        get => _autoScrollEnabled;
        set { _autoScrollEnabled = value; OnPropertyChanged(); }
    }

    public LyricLine? CenterLine
    {
        get => _centerLine;
        set { _centerLine = value; OnPropertyChanged(); }
    }

    // Event fired when active line changes — UI can scroll to it
    public event Action<int>? ActiveLineChanged;
    public event Action<string>? RequestNavigate;

    public bool HasLyrics => DisplayedLyrics.Count > 0;

    public MainViewModel()
    {
        _lyricsService = new LyricsService();
    }

    private double _lyricOffset; // seconds, can be negative

    public double LyricOffset
    {
        get => _lyricOffset;
        set { _lyricOffset = Math.Round(value, 1); OnPropertyChanged(); OnPropertyChanged(nameof(LyricOffsetText)); }
    }

    public string LyricOffsetText => _lyricOffset == 0 ? "0.0s" : $"{_lyricOffset:+0.0;-0.0}s";

    public void AdjustOffset(double delta) => LyricOffset = _lyricOffset + delta;

    // Called from WebView2 JavaScript bridge with current playback seconds
    public void UpdatePlaybackTime(double seconds)
    {
        _currentTime = seconds;
        SyncLyrics(TimeSpan.FromSeconds(seconds));
    }

    private void SyncLyrics(TimeSpan position)
    {
        if (_allLyrics.Count == 0) return;

        // Apply offset: positive = lyrics ahead (shift position forward), negative = lyrics behind
        var adjusted = position + TimeSpan.FromSeconds(_lyricOffset);

        int newActive = -1;
        for (int i = _allLyrics.Count - 1; i >= 0; i--)
        {
            if (_allLyrics[i].Timestamp <= adjusted)
            {
                newActive = i;
                break;
            }
        }

        if (newActive == _activeLine) return;
        _activeLine = newActive;

        Application.Current.Dispatcher.Invoke(() =>
        {
            for (int i = 0; i < DisplayedLyrics.Count; i++)
                DisplayedLyrics[i].IsActive = (i == _activeLine);

            CenterLine = _activeLine >= 0 ? DisplayedLyrics[_activeLine] : null;

            if (_activeLine >= 0 && AutoScrollEnabled)
                ActiveLineChanged?.Invoke(_activeLine);
        });
    }

    public async Task LoadFromUrlAsync(string url)
    {
        var videoId = YouTubeService.ExtractVideoId(url);
        if (videoId == null)
        {
            StatusText = "❌ Invalid YouTube URL";
            return;
        }

        EmbedUrl = YouTubeService.BuildEmbedUrl(videoId);
        StatusText = "▶ Video loaded — searching for lyrics...";

        // Parse title from URL if possible, else wait for title from JS bridge
        if (!string.IsNullOrWhiteSpace(ArtistInput) && !string.IsNullOrWhiteSpace(TrackInput))
        {
            await FetchLyricsAsync(ArtistInput, TrackInput);
        }
        else
        {
            StatusText = "▶ Video loaded — fill Artist/Track fields to load lyrics";
        }
    }

    public void OnVideoTitleReceived(string title)
    {
        VideoTitle = title;
        var (artist, track) = YouTubeService.ParseVideoTitle(title);
        if (artist != null) ArtistInput = artist;
        track = track.Replace("- YouTube", "", StringComparison.OrdinalIgnoreCase).Trim();
        TrackInput = track;
        StatusText = $"🎵 Detected: {artist ?? "Unknown"} — {track}";

        // Auto-fetch
        _ = FetchLyricsAsync(artist ?? "", track);
    }

    public async Task FetchLyricsAsync(string artist, string track)
    {
        if (string.IsNullOrWhiteSpace(track))
        {
            StatusText = "⚠ Enter a track name";
            return;
        }

        IsLoadingLyrics = true;
        StatusText = $"🔍 Searching lyrics for: {artist} — {track}";

        var lyrics = await _lyricsService.SearchLyricsAsync(artist, track);

        IsLoadingLyrics = false;

        if (lyrics == null || lyrics.Count == 0)
        {
            StatusText = "❌ No lyrics found — try adjusting Artist/Track";
            IsSynced = false;
            return;
        }

        _allLyrics = lyrics;
        _activeLine = -1;

        Application.Current.Dispatcher.Invoke(() =>
        {
            DisplayedLyrics.Clear();
            foreach (var line in _allLyrics)
                DisplayedLyrics.Add(line);
            OnPropertyChanged(nameof(HasLyrics));
        });

        IsSynced = lyrics.Any(l => l.Timestamp > TimeSpan.Zero);
        StatusText = IsSynced
            ? $"✓ Synced lyrics loaded ({lyrics.Count} lines)"
            : $"✓ Plain lyrics loaded ({lyrics.Count} lines)";
    }

    public void OpenYouTubeSearch(string query)
    {
        var url = YouTubeService.BuildSearchUrl(query);
        RequestNavigate?.Invoke(url);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
