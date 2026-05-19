using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CosmicLyrics.ViewModels;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace CosmicLyrics.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _webViewReady;

    // JavaScript injected into YouTube page to poll playback time
    private const string BridgeScript = """
        (function() {
            if (window._cosmicBridgeAttached) return;
            window._cosmicBridgeAttached = true;

            function getPlayer() { return document.querySelector('video'); }

            setInterval(function() {
                var v = getPlayer();
                if (!v) return;
                window.chrome.webview.postMessage(JSON.stringify({
                    type: 'timeupdate',
                    currentTime: v.currentTime,
                    duration: v.duration,
                    paused: v.paused
                }));
            }, 250);
        })();
        """;

    // CSS injected to hide YouTube ad overlays and skip-ad UI
    private const string AdBlockCss = """
        .ad-showing .html5-main-video { opacity: 1 !important; }
        .ytp-ad-module, .ytp-ad-overlay-container, .ytp-ad-text-overlay,
        .ytp-ad-skip-button-container, .ytp-ad-player-overlay,
        .ytp-ad-progress, .ytp-ad-progress-list,
        #masthead-ad, ytd-banner-promo-renderer,
        ytd-statement-banner-renderer, ytd-ad-slot-renderer,
        ytd-promoted-video-renderer, ytd-compact-promoted-video-renderer,
        ytd-display-ad-renderer, ytd-in-feed-ad-layout-renderer,
        tp-yt-paper-dialog[aria-label*="ad" i],
        .ytd-merch-shelf-renderer, #player-ads,
        ytd-action-companion-ad-renderer { display: none !important; }
        """;

    // Ad/tracker domains to block at network level
    private static readonly string[] BlockedDomains =
    {
        "doubleclick.net", "googlesyndication.com", "googleadservices.com",
        "googleads.g.doubleclick.net", "adservice.google.com",
        "adservice.google.co.uk", "imasdk.googleapis.com",
        "ads.youtube.com", "static.doubleclick.net",
        "pagead2.googlesyndication.com", "tpc.googlesyndication.com",
        "yt3.ggpht.com/ytad", "youtube.com/pagead",
        "youtube.com/api/stats/ads"
    };

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.ActiveLineChanged += OnActiveLyricLineChanged;
        _vm.RequestNavigate += OnRequestNavigate;

        InitializeWebView();
    }

    private async void InitializeWebView()
    {
        await YoutubePlayer.EnsureCoreWebView2Async();
        _webViewReady = true;

        var wv = YoutubePlayer.CoreWebView2;

        // ── Ad blocker: block known ad/tracker domains at network level ──────
        wv.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        wv.WebResourceRequested += OnWebResourceRequested;

        wv.NavigationCompleted += OnNavigationCompleted;
        wv.WebMessageReceived += OnWebMessageReceived;
        wv.DocumentTitleChanged += (s, e) =>
        {
            var title = YoutubePlayer.CoreWebView2.DocumentTitle;
            if (!string.IsNullOrWhiteSpace(title) && title != "YouTube")
                _vm.OnVideoTitleReceived(title);
        };
        wv.Settings.IsScriptEnabled = true;
        wv.Settings.AreDefaultScriptDialogsEnabled = false;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = e.Request.Uri;
        foreach (var domain in BlockedDomains)
        {
            if (uri.Contains(domain, StringComparison.OrdinalIgnoreCase))
            {
                // Return empty 200 response — silently swallows the request
                e.Response = YoutubePlayer.CoreWebView2.Environment.CreateWebResourceResponse(
                    null, 200, "OK", "Content-Type: text/plain");
                return;
            }
        }
    }

    // ─── Navigation ─────────────────────────────────────────────────────────

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var wv = YoutubePlayer.CoreWebView2;

        // Inject CSS to hide any ad UI that slipped through the network block
        await wv.ExecuteScriptAsync(
    "(function() {" +
    "  var s = document.getElementById('_cosmicAdBlockCss');" +
    "  if (s) return;" +
    "  s = document.createElement('style');" +
    "  s.id = '_cosmicAdBlockCss';" +
    "  s.textContent = '.ytp-ad-module, .ytp-ad-overlay-container, .ytp-ad-text-overlay, .ytp-ad-skip-button-container, .ytp-ad-player-overlay, .ytp-ad-progress, #masthead-ad, ytd-banner-promo-renderer, ytd-statement-banner-renderer, ytd-ad-slot-renderer, ytd-promoted-video-renderer, ytd-compact-promoted-video-renderer, ytd-display-ad-renderer, ytd-in-feed-ad-layout-renderer, #player-ads, ytd-action-companion-ad-renderer { display: none !important; }';" +
    "  document.head.appendChild(s);" +
    "})();");

        // Auto-skip any video ad that manages to start
        await wv.ExecuteScriptAsync(
            "(function() {" +
            "  if (window._cosmicAdSkipAttached) return;" +
            "  window._cosmicAdSkipAttached = true;" +
            "  setInterval(function() {" +
            "    var player = document.querySelector('.html5-video-player');" +
            "    if (!player || !player.classList.contains('ad-showing')) return;" +
            "    var skip = player.querySelector('.ytp-skip-ad-button, .ytp-ad-skip-button');" +
            "    if (skip) { skip.click(); return; }" +
            "    var vid = player.querySelector('video');" +
            "    if (vid && vid.duration) vid.currentTime = vid.duration;" +
            "  }, 300);" +
            "})();");

        // Time-sync bridge
        await wv.ExecuteScriptAsync(BridgeScript);
    }


    private void OnPageTitleChanged(object? sender, EventArgs e)
    {
        var title = YoutubePlayer.CoreWebView2.DocumentTitle;
        if (!string.IsNullOrWhiteSpace(title) && title != "YouTube")
            _vm.OnVideoTitleReceived(title);
    }

    private void OnRequestNavigate(string url)
    {
        if (_webViewReady)
            YoutubePlayer.CoreWebView2.Navigate(url);
    }

    // ─── Playback time bridge ────────────────────────────────────────────────

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = JObject.Parse(e.TryGetWebMessageAsString());
            if (json["type"]?.ToString() == "timeupdate")
            {
                var t = json["currentTime"]?.Value<double>() ?? 0;
                _vm.UpdatePlaybackTime(t);
            }
        }
        catch { /* ignore malformed messages */ }
    }

    // ─── Lyric scrolling ─────────────────────────────────────────────────────

    private void OnActiveLyricLineChanged(int lineIndex)
    {
        if (!_vm.AutoScrollEnabled) return;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            var container = LyricsPanel.ItemContainerGenerator.ContainerFromIndex(lineIndex) as FrameworkElement;
            if (container == null) return;

            // Get the item's Y position relative to the ScrollViewer's scrollable content
            var transform = container.TransformToAncestor(LyricsScrollViewer);
            var itemPosInViewport = transform.Transform(new Point(0, 0)).Y;

            // itemPosInViewport is already relative to the visible viewport top.
            // To center it: current scroll + itemPosInViewport - half viewport + half item height
            var viewportHeight = LyricsScrollViewer.ViewportHeight;
            var itemHeight = container.ActualHeight;
            var targetOffset = LyricsScrollViewer.VerticalOffset
                               + itemPosInViewport
                               - (viewportHeight / 2)
                               + (itemHeight / 2);

            SmoothScrollTo(LyricsScrollViewer, targetOffset);
        }));
    }

    private void SmoothScrollTo(ScrollViewer sv, double targetOffset)
    {
        var from = sv.VerticalOffset;
        var anim = new DoubleAnimation(from, targetOffset,
            new Duration(TimeSpan.FromMilliseconds(450)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        var clock = anim.CreateClock();
        clock.Controller?.Begin();

        CompositionTarget.Rendering += OnRenderFrame;
        void OnRenderFrame(object? s, EventArgs e)
        {
            if (clock.CurrentState == System.Windows.Media.Animation.ClockState.Active)
            {
                sv.ScrollToVerticalOffset(from + (targetOffset - from) * (clock.CurrentProgress ?? 0));
            }
            else
            {
                sv.ScrollToVerticalOffset(targetOffset);
                CompositionTarget.Rendering -= OnRenderFrame;
            }
        }
    }

    // ─── UI Event Handlers ───────────────────────────────────────────────────

    private void OffsetMinus_Click(object sender, RoutedEventArgs e) => _vm.AdjustOffset(-0.5);
    private void OffsetPlus_Click(object sender, RoutedEventArgs e)  => _vm.AdjustOffset(+0.5);
    private void OffsetReset_Click(object sender, RoutedEventArgs e) => _vm.LyricOffset = 0;

    private async void LoadUrl_Click(object sender, RoutedEventArgs e)
    {
        await LoadCurrentInput();
    }

    private async void UrlInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await LoadCurrentInput();
    }

    private async Task LoadCurrentInput()
    {
        var text = _vm.SearchText?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // Is it a URL?
        if (text.Contains("youtube.com") || text.Contains("youtu.be"))
        {
            VideoPlaceholder.Visibility = Visibility.Collapsed;
            YoutubePlayer.Visibility = Visibility.Visible;

            if (_webViewReady)
            {
                // Navigate to the full YouTube watch page for JS time bridging
                YoutubePlayer.CoreWebView2.Navigate(text);
            }

            await _vm.LoadFromUrlAsync(text);
        }
        else
        {
            // Treat as search
            VideoPlaceholder.Visibility = Visibility.Collapsed;
            YoutubePlayer.Visibility = Visibility.Visible;
            _vm.OpenYouTubeSearch(text);
        }
    }

    private async void FetchLyrics_Click(object sender, RoutedEventArgs e)
    {
        await _vm.FetchLyricsAsync(_vm.ArtistInput, _vm.TrackInput);
    }

    private async void LyricsSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await _vm.FetchLyricsAsync(_vm.ArtistInput, _vm.TrackInput);
    }

    // ─── Window chrome ────────────────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
        => Close();
}


