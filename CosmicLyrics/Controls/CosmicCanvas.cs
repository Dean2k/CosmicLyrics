using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CosmicLyrics.Controls;

/// <summary>
/// Animated starfield/nebula canvas that forms the cosmic lyric background.
/// Renders two layers: static tiny stars + drifting nebula blobs.
/// </summary>
public class CosmicCanvas : Canvas
{
    private readonly DispatcherTimer _timer;
    private readonly Random _rng = new(42);
    private readonly List<StarParticle> _stars = new();
    private readonly List<NebulaBlob> _blobs = new();
    private double _phase;

    // Nebula gradient overlay (rendered on top via DrawingBrush)
    private readonly DrawingGroup _nebulaDrawing = new();

    private const int StarCount = 180;
    private const int BlobCount = 6;

    public CosmicCanvas()
    {
        Background = new SolidColorBrush(Color.FromRgb(8, 6, 18));
        ClipToBounds = true;

        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30fps
        _timer.Tick += OnTick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeParticles();
        _timer.Start();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        InitializeParticles();
    }

    private void InitializeParticles()
    {
        Children.Clear();
        _stars.Clear();
        _blobs.Clear();

        double w = ActualWidth > 0 ? ActualWidth : 800;
        double h = ActualHeight > 0 ? ActualHeight : 600;

        // Stars
        for (int i = 0; i < StarCount; i++)
        {
            double size = _rng.NextDouble() * 2.5 + 0.5;
            byte brightness = (byte)_rng.Next(100, 255);
            var el = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(Color.FromArgb(brightness, 240, 244, 255)),
                IsHitTestVisible = false
            };

            double x = _rng.NextDouble() * w;
            double y = _rng.NextDouble() * h;
            SetLeft(el, x);
            SetTop(el, y);
            Children.Add(el);

            _stars.Add(new StarParticle
            {
                Element = el,
                X = x, Y = y,
                BaseAlpha = brightness,
                TwinkleSpeed = _rng.NextDouble() * 0.05 + 0.01,
                TwinkleOffset = _rng.NextDouble() * Math.PI * 2,
                DriftX = (_rng.NextDouble() - 0.5) * 0.08,
                DriftY = (_rng.NextDouble() - 0.5) * 0.08
            });
        }

        // Nebula blobs — large soft ellipses
        Color[] nebulaPalette = new[]
        {
            Color.FromArgb(18, 0, 80, 200),
            Color.FromArgb(20, 80, 0, 160),
            Color.FromArgb(15, 0, 160, 180),
            Color.FromArgb(22, 120, 0, 80),
            Color.FromArgb(16, 0, 100, 120),
            Color.FromArgb(20, 60, 20, 140),
        };

        for (int i = 0; i < BlobCount; i++)
        {
            double bw = _rng.NextDouble() * 300 + 150;
            double bh = _rng.NextDouble() * 200 + 100;
            var color = nebulaPalette[i % nebulaPalette.Length];

            var blob = new Ellipse
            {
                Width = bw, Height = bh,
                Fill = new RadialGradientBrush(
                    color,
                    Color.FromArgb(0, color.R, color.G, color.B)),
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(_rng.NextDouble() * 360)
            };

            double x = _rng.NextDouble() * w;
            double y = _rng.NextDouble() * h;
            SetLeft(blob, x - bw / 2);
            SetTop(blob, y - bh / 2);
            Children.Add(blob);

            _blobs.Add(new NebulaBlob
            {
                Element = blob,
                X = x, Y = y,
                VX = (_rng.NextDouble() - 0.5) * 0.25,
                VY = (_rng.NextDouble() - 0.5) * 0.15,
                RotationSpeed = (_rng.NextDouble() - 0.5) * 0.1
            });
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _phase += 0.016;
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Animate stars (twinkle + gentle drift)
        for (int i = 0; i < _stars.Count; i++)
        {
            var s = _stars[i];
            double twinkle = Math.Sin(_phase * s.TwinkleSpeed * 60 + s.TwinkleOffset);
            byte alpha = (byte)Math.Clamp(s.BaseAlpha + twinkle * 60, 30, 255);

            if (s.Element.Fill is SolidColorBrush brush)
            {
                var c = brush.Color;
                s.Element.Fill = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            }

            s.X += s.DriftX;
            s.Y += s.DriftY;
            if (s.X < -2) s.X = w + 2;
            if (s.X > w + 2) s.X = -2;
            if (s.Y < -2) s.Y = h + 2;
            if (s.Y > h + 2) s.Y = -2;

            SetLeft(s.Element, s.X);
            SetTop(s.Element, s.Y);
        }

        // Animate nebula blobs
        for (int i = 0; i < _blobs.Count; i++)
        {
            var b = _blobs[i];
            b.X += b.VX;
            b.Y += b.VY;

            // Wrap around
            if (b.X < -b.Element.Width) b.X = w + b.Element.Width / 2;
            if (b.X > w + b.Element.Width) b.X = -b.Element.Width / 2;
            if (b.Y < -b.Element.Height) b.Y = h + b.Element.Height / 2;
            if (b.Y > h + b.Element.Height) b.Y = -b.Element.Height / 2;

            SetLeft(b.Element, b.X - b.Element.Width / 2);
            SetTop(b.Element, b.Y - b.Element.Height / 2);

            if (b.Element.RenderTransform is RotateTransform rt)
                rt.Angle += b.RotationSpeed;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
    }

    private class StarParticle
    {
        public Ellipse Element = null!;
        public double X, Y;
        public byte BaseAlpha;
        public double TwinkleSpeed, TwinkleOffset;
        public double DriftX, DriftY;
    }

    private class NebulaBlob
    {
        public Ellipse Element = null!;
        public double X, Y;
        public double VX, VY;
        public double RotationSpeed;
    }
}
