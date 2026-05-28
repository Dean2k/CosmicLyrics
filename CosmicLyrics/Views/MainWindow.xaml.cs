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
    // Expanded to cover most known YouTube/Google ad & tracking infrastructure
    private static readonly string[] BlockedDomains =
    {
        // Google / DoubleClick core
        "doubleclick.net", "googlesyndication.com", "googleadservices.com",
        "googleads.g.doubleclick.net", "adservice.google.",
        "pagead2.googlesyndication.com", "tpc.googlesyndication.com",
        "static.doubleclick.net", "securepubads.g.doubleclick.net",
        "pubads.g.doubleclick.net", "g.doubleclick.net",
        "www.googleadservices.com",

        // YouTube ads
        "ads.youtube.com", "youtube.com/pagead", "youtube.com/api/stats/ads",
        "/api/stats/ads", "/pagead", "/get_video_info?ad",

        // IMA SDK / VAST
        "imasdk.googleapis.com", "sdk.iad.google.com",
        "googleusercontent.com/ads",

        // Analytics / telemetry (reduces fingerprinting)
        "google-analytics.com", "googletagmanager.com", "googletagservices.com",
        "firebase.google.com", "analytics.google.com", "analytics.youtube.com",
        "stats.g.doubleclick.net", "www.google-analytics.com",
        "ssl.google-analytics.com", "googleoptimize.com",

        // Other trackers
        "facebook.com/tr", "connect.facebook.net", "google.com/pagead",
        "google.com/aclk", "google.com/ads", "google.com/recaptcha/api.js",
        "gstatic.com/ads", "gstatic.com/recaptcha",

        // Generic ad patterns
        "/adsense", "/adsystem", "/advertisement", "/advert", "/adserver",
        "/bannerad", "/popunder", "/pop-up", "/tracking", "/telemetry",
        "/metrics", "/log_event", "/clientlog", "/watchtime",
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

    // This script runs BEFORE any page scripts on every document creation.
    // It intercepts fetch/XHR responses and strips ad data, and also traps
    // ytInitialPlayerResponse so ads are removed before YouTube's player reads them.
    private const string AdBlockCoreScript = """
        (function() {
            if (window._cosmicAdBlockCore) return;
            window._cosmicAdBlockCore = true;

            function stripAds(text) {
                if (!text || typeof text !== 'string') return text;
                // Simple string replacement — same principle uBlock Origin uses
                text = text.split('"adPlacements"').join('"no_ads"');
                text = text.split('"adSlots"').join('"no_ads"');
                text = text.split('"playerAds"').join('"no_ads"');
                text = text.split('"adBreakHeartbeatParams"').join('"no_ads"');
                return text;
            }

            // Hook fetch()
            var _origFetch = window.fetch;
            window.fetch = function(url, opts) {
                var u = (url || '').toString();
                var isYt = u.indexOf('youtube.com') !== -1 || u.indexOf('googlevideo.com') !== -1 || u.indexOf('youtubei') !== -1;
                if (!isYt) return _origFetch.apply(this, arguments);
                return _origFetch.apply(this, arguments).then(function(resp) {
                    if (!resp.ok) return resp;
                    var ct = (resp.headers.get('content-type') || '').toLowerCase();
                    if (ct.indexOf('json') === -1 && ct.indexOf('text') === -1) return resp;
                    return resp.text().then(function(body) {
                        var clean = stripAds(body);
                        return new Response(clean, { status: resp.status, statusText: resp.statusText, headers: resp.headers });
                    });
                });
            };

            // Hook XMLHttpRequest
            var _origOpen = XMLHttpRequest.prototype.open;
            var _origSend = XMLHttpRequest.prototype.send;
            XMLHttpRequest.prototype.open = function(method, url) {
                this._url = (url || '').toString();
                return _origOpen.apply(this, arguments);
            };
            XMLHttpRequest.prototype.send = function() {
                var xhr = this;
                var url = xhr._url || '';
                var isYt = url.indexOf('youtube.com') !== -1 || url.indexOf('googlevideo.com') !== -1 || url.indexOf('youtubei') !== -1;
                if (!isYt) return _origSend.apply(this, arguments);
                var _onReady = function() {
                    if (xhr.readyState === 4 && xhr.responseType === '' && typeof xhr.responseText === 'string') {
                        try {
                            var clean = stripAds(xhr.responseText);
                            Object.defineProperty(xhr, 'responseText', {value: clean, writable: true});
                            Object.defineProperty(xhr, 'response', {value: clean, writable: true});
                        } catch(e) {}
                    }
                };
                xhr.addEventListener('readystatechange', _onReady);
                return _origSend.apply(this, arguments);
            };

            // Trap ytInitialPlayerResponse so we can strip ads BEFORE the player reads it
            var _ytInitialPlayerResponse = null;
            Object.defineProperty(window, 'ytInitialPlayerResponse', {
                configurable: true,
                get: function() { return _ytInitialPlayerResponse; },
                set: function(val) {
                    if (val && typeof val === 'object') {
                        try { delete val.adPlacements; } catch(e) {}
                        try { delete val.playerAds; } catch(e) {}
                        try { delete val.adSlots; } catch(e) {}
                    }
                    _ytInitialPlayerResponse = val;
                }
            });

            // Also trap ytplayer.config.args.player_response if present
            try {
                var _pr = null;
                Object.defineProperty(window, 'playerResponse', {
                    configurable: true,
                    get: function() { return _pr; },
                    set: function(val) {
                        if (val && typeof val === 'object') {
                            try { delete val.adPlacements; } catch(e) {}
                            try { delete val.playerAds; } catch(e) {}
                            try { delete val.adSlots; } catch(e) {}
                        }
                        _pr = val;
                    }
                });
            } catch(e) {}
        })();
        """;

    private async void InitializeWebView()
    {
        await YoutubePlayer.EnsureCoreWebView2Async();
        _webViewReady = true;

        var wv = YoutubePlayer.CoreWebView2;

        // ── Ad blocker: block known ad/tracker domains at network level ──────
        wv.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        wv.WebResourceRequested += OnWebResourceRequested;

        // Inject core ad-block script BEFORE any page scripts run (catches initial player data)
        await wv.AddScriptToExecuteOnDocumentCreatedAsync(AdBlockCoreScript);

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
        foreach (var pattern in BlockedDomains)
        {
            // Some patterns are path fragments (start with "/"), others are domains
            bool match = pattern.StartsWith('/')
                ? uri.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                : uri.Contains(pattern, StringComparison.OrdinalIgnoreCase);

            if (match)
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

        // ── 1. Inject comprehensive ad-block CSS ────────────────────────────────
        await wv.ExecuteScriptAsync(
    "(function() {" +
    "  var s = document.getElementById('_cosmicAdBlockCss');" +
    "  if (s) return;" +
    "  s = document.createElement('style');" +
    "  s.id = '_cosmicAdBlockCss';" +
    "  s.textContent = '" +
    "    .ytp-ad-module,.ytp-ad-overlay-container,.ytp-ad-text-overlay," +
    "    .ytp-ad-skip-button-container,.ytp-ad-player-overlay,.ytp-ad-progress," +
    "    .ytp-ad-progress-list,.ytp-ad-feedback-dialog,.ytp-ad-info-hover-container," +
    "    .ytp-ad-preview-container,.ytp-ad-message-container,.ytp-ad-timed-pie-countdown," +
    "    .ytp-ad-duration-remaining,.ytp-ad-visit-advertiser-link," +
    "    #masthead-ad,ytd-banner-promo-renderer,ytd-statement-banner-renderer," +
    "    ytd-ad-slot-renderer,ytd-promoted-video-renderer," +
    "    ytd-compact-promoted-video-renderer,ytd-display-ad-renderer," +
    "    ytd-in-feed-ad-layout-renderer,tp-yt-paper-dialog[aria-label*=\\\"ad\\\" i]," +
    "    .ytd-merch-shelf-renderer,#player-ads,ytd-action-companion-ad-renderer," +
    "    ytd-popup-container,ytd-engagement-panel-section-list-renderer[panel-identifier=\\\"engagement-panel-ads\\\"]," +
    "    ytd-engagement-panel-section-list-renderer[target-id=\\\"engagement-panel-ads\\\"]," +
    "    .ytd-paid-promotion-overlay-renderer,.ytd-player-legacy-desktop-watch-ads-renderer," +
    "    .ytd-video-masthead-ad-advanced-config-renderer,.ytd-video-masthead-ad-v3-renderer," +
    "    #player-ads-container,.sparkles-light-cta,.sparkles-light-promotion," +
    "    .video-ads,.ad-container,#ad-image-container,#ad-ytplayer," +
    "    .ytp-ad-overlay-image,.ytp-ad-overlay-slot,.ytp-ad-overlay-video-masthead," +
    "    .ytp-cards-teaser,.ytp-cards-button,.ytp-ce-element," +
    "    .ytp-flyout-cta,.ytp-title-channel,.ytp-title,.ytp-pause-overlay," +
    "    .ytp-cards-collection { display:none !important; }" +
    "    .ad-showing .html5-main-video { opacity:1 !important; }'" +
    "  ;" +
    "  document.head.appendChild(s);" +
    "})();");

        // ── 2. Inject ad UI skip/cleanup script ───────────────────────────────
        await wv.ExecuteScriptAsync(
            "(function() {" +
            "  if (window._cosmicAdSkipAttached) return;" +
            "  window._cosmicAdSkipAttached = true;" +
            "" +
            "  setInterval(function() {" +
            "    var player = document.querySelector('.html5-video-player');" +
            "    if (!player) return;" +
            "" +
            "    // Click skip-ad button as soon as it appears" +
            "    var skip = player.querySelector('.ytp-skip-ad-button, .ytp-ad-skip-button, .ytp-skip-ad-button__text');" +
            "    if (skip) skip.click();" +
            "" +
            "    // Close overlay ads" +
            "    var closeBtn = player.querySelector('.ytp-ad-overlay-close-button, .ytp-ad-close-button');" +
            "    if (closeBtn) closeBtn.click();" +
            "" +
            "    // Fast-forward in-stream video ads" +
            "    if (player.classList.contains('ad-showing')) {" +
            "      var vid = player.querySelector('video');" +
            "      if (vid && vid.duration && isFinite(vid.duration)) {" +
            "        try { vid.currentTime = vid.duration; } catch(e) {}" +
            "      }" +
            "    }" +
            "  }, 250);" +
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


