using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace NovaClient.Launcher.Common;

/// <summary>
/// Lightweight decorative starfield: scatters small twinkling dots across its area, each with its
/// own randomized size, brightness and rhythm. Fixed seed → same sky every run. Purely visual.
/// </summary>
public sealed class StarfieldCanvas : Canvas
{
    public static readonly DependencyProperty StarCountProperty = DependencyProperty.Register(
        nameof(StarCount), typeof(int), typeof(StarfieldCanvas), new PropertyMetadata(28));

    public int StarCount
    {
        get => (int)GetValue(StarCountProperty);
        set => SetValue(StarCountProperty, value);
    }

    private readonly List<(Ellipse Dot, double FracX, double FracY)> _stars = new();
    private bool _built;

    public StarfieldCanvas()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Loaded += (_, _) => Build();
        SizeChanged += (_, _) => Reposition();
    }

    private void Build()
    {
        if (_built) return;
        _built = true;
        var random = new Random(20260720); // fixed seed: a stable, hand-tuned sky

        for (var i = 0; i < StarCount; i++)
        {
            var size = 1.0 + random.NextDouble() * 1.8;
            var peak = 0.10 + random.NextDouble() * 0.38;
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = Brushes.White,
                Opacity = 0
            };
            _stars.Add((dot, random.NextDouble(), random.NextDouble()));
            Children.Add(dot);

            var twinkle = new DoubleAnimation(0.02, peak, TimeSpan.FromSeconds(0.9 + random.NextDouble() * 2.2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(random.NextDouble() * 6),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            dot.BeginAnimation(OpacityProperty, twinkle);
        }
        Reposition();
    }

    private void Reposition()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        foreach (var (dot, fracX, fracY) in _stars)
        {
            SetLeft(dot, fracX * ActualWidth);
            SetTop(dot, fracY * ActualHeight);
        }
    }
}
