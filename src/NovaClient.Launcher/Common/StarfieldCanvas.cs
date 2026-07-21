using System.Windows;
using System.Windows.Media;

namespace NovaClient.Launcher.Common;

/// <summary>
/// Decorative twinkling starfield drawn on a single visual via one composition-driven render
/// loop, so it stays smooth even with thousands of stars (a per-element animation approach would
/// choke). Fixed seed → the same sky every run. Purely visual, no hit-testing.
/// </summary>
public sealed class StarfieldCanvas : FrameworkElement
{
    public static readonly DependencyProperty StarCountProperty = DependencyProperty.Register(
        nameof(StarCount), typeof(int), typeof(StarfieldCanvas),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((StarfieldCanvas)d).Rebuild()));

    public int StarCount
    {
        get => (int)GetValue(StarCountProperty);
        set => SetValue(StarCountProperty, value);
    }

    private readonly record struct Star(double FracX, double FracY, double Radius, double Peak, double Phase, double Speed);

    private Star[] _stars = Array.Empty<Star>();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _lastFrame;
    // Twinkles are slow, so ~30fps looks identical to 60 and halves idle CPU.
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);

    // Pre-frozen brushes at stepped opacities: a shared mutable brush can't vary per star, and
    // per-star PushOpacity would be slow. 48 buckets is visually seamless.
    private const int Buckets = 48;
    private static readonly SolidColorBrush[] BrushPool = BuildPool();

    private bool _running;

    public StarfieldCanvas()
    {
        IsHitTestVisible = false;
        Rebuild();
        Loaded += OnLoaded;
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => UpdateRunning();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is not null)
        {
            // Twinkle only while the launcher is the active foreground window — no CPU spent
            // animating an invisible background while the player is in-game.
            window.Activated += (_, _) => UpdateRunning();
            window.Deactivated += (_, _) => UpdateRunning();
            window.StateChanged += (_, _) => UpdateRunning();
        }
        UpdateRunning();
    }

    private void UpdateRunning()
    {
        var window = Window.GetWindow(this);
        var shouldRun = IsVisible
                        && window is { IsActive: true, WindowState: not WindowState.Minimized };
        if (shouldRun) Start();
        else Stop();
    }

    private void Start()
    {
        if (_running) return;
        _running = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private static SolidColorBrush[] BuildPool()
    {
        var pool = new SolidColorBrush[Buckets];
        for (var i = 0; i < Buckets; i++)
        {
            var brush = new SolidColorBrush(Colors.White) { Opacity = (i + 1) / (double)Buckets };
            brush.Freeze();
            pool[i] = brush;
        }
        return pool;
    }

    private void Rebuild()
    {
        var random = new Random(20260720); // fixed seed: a stable, hand-tuned sky
        var count = Math.Max(0, StarCount);
        _stars = new Star[count];
        for (var i = 0; i < count; i++)
        {
            _stars[i] = new Star(
                random.NextDouble(),
                random.NextDouble(),
                0.5 + random.NextDouble() * 1.1,
                0.10 + random.NextDouble() * 0.42,
                random.NextDouble() * Math.PI * 2,
                0.15 + random.NextDouble() * 0.5);
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        if (now - _lastFrame < FrameInterval) return;
        _lastFrame = now;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || _stars.Length == 0) return;

        var t = _clock.Elapsed.TotalSeconds;
        foreach (var star in _stars)
        {
            // Sine twinkle in [0.02 .. peak]; each star has its own phase and speed.
            var wave = (Math.Sin(t * star.Speed + star.Phase) + 1) * 0.5;
            var opacity = 0.02 + wave * star.Peak;
            var bucket = Math.Clamp((int)(opacity * Buckets), 0, Buckets - 1);
            dc.DrawEllipse(BrushPool[bucket], null, new Point(star.FracX * w, star.FracY * h), star.Radius, star.Radius);
        }
    }
}
