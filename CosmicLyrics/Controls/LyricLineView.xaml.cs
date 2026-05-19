using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace CosmicLyrics.Controls;

public partial class LyricLineView : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(LyricLineView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(LyricLineView),
            new PropertyMetadata(false, OnIsActiveChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private static readonly DropShadowEffect LineGlow = new()
    {
        Color = Color.FromRgb(100, 220, 255),
        BlurRadius = 16,
        ShadowDepth = 0,
        Opacity = 0.85,
        RenderingBias = RenderingBias.Performance
    };

    public LyricLineView()
    {
        InitializeComponent();
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LyricLineView v) v.UpdateLineState((bool)e.NewValue);
    }

    private void UpdateLineState(bool active)
    {
        var sb = (Storyboard)Resources[active ? "ActivateAnimation" : "DeactivateAnimation"];
        sb.Begin(this);

        if (active)
        {
            MainText.FontSize = 23;
            MainText.FontWeight = FontWeights.ExtraBold;
            MainText.Effect = LineGlow;
        }
        else
        {
            MainText.FontSize = 18;
            MainText.FontWeight = FontWeights.Bold;
            MainText.Effect = null;
        }
    }
}

