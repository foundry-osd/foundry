using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Foundry.Deploy.Controls;

public partial class CustomProgressRing : UserControl
{
    private const double StartAngle = -90d;
    private const double IndeterminateSweep = 120d;

    private static readonly DoubleAnimation IndeterminateRotationAnimation = new()
    {
        From = 0d,
        To = 360d,
        Duration = TimeSpan.FromSeconds(1.2d),
        RepeatBehavior = RepeatBehavior.Forever
    };

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(CustomProgressRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate),
        typeof(bool),
        typeof(CustomProgressRing),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualPropertyChanged));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness),
        typeof(double),
        typeof(CustomProgressRing),
        new FrameworkPropertyMetadata(14d, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualPropertyChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(CustomProgressRing),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualPropertyChanged));

    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush),
        typeof(Brush),
        typeof(CustomProgressRing),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualPropertyChanged));

    public CustomProgressRing()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        Unloaded += OnUnloaded;
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush ProgressBrush
    {
        get => (Brush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CustomProgressRing ring)
        {
            ring.UpdateVisuals();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateVisuals();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVisuals();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopIndeterminateAnimation();
    }

    private void UpdateVisuals()
    {
        double width = Math.Max(0d, ActualWidth);
        double height = Math.Max(0d, ActualHeight);
        double diameter = Math.Min(width, height);
        double clampedThickness = Math.Max(1d, Thickness);

        if (diameter <= clampedThickness)
        {
            DeterminateArc.Data = Geometry.Empty;
            IndeterminateArc.Data = Geometry.Empty;
            return;
        }

        double radius = (diameter - clampedThickness) / 2d;
        Point center = new(width / 2d, height / 2d);
        IndeterminateRotateTransform.CenterX = center.X;
        IndeterminateRotateTransform.CenterY = center.Y;

        if (IsIndeterminate)
        {
            DeterminateArc.Visibility = Visibility.Collapsed;
            IndeterminateArc.Visibility = Visibility.Visible;
            DeterminateArc.Data = Geometry.Empty;
            IndeterminateArc.Data = CreateArcGeometry(center, radius, StartAngle, StartAngle + IndeterminateSweep);
            StartIndeterminateAnimation();
            return;
        }

        IndeterminateArc.Visibility = Visibility.Collapsed;
        DeterminateArc.Visibility = Visibility.Visible;
        IndeterminateArc.Data = Geometry.Empty;
        DeterminateArc.Data = CreateProgressGeometry(center, radius, Value);
        StopIndeterminateAnimation();
    }

    private void StartIndeterminateAnimation()
    {
        IndeterminateRotateTransform.BeginAnimation(RotateTransform.AngleProperty, IndeterminateRotationAnimation);
    }

    private void StopIndeterminateAnimation()
    {
        IndeterminateRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private static Geometry CreateProgressGeometry(Point center, double radius, double value)
    {
        double clampedValue = Math.Clamp(value, 0d, 100d);
        if (clampedValue <= 0d)
        {
            return Geometry.Empty;
        }

        if (clampedValue >= 100d)
        {
            return new EllipseGeometry(center, radius, radius);
        }

        double endAngle = StartAngle + (clampedValue / 100d * 360d);
        return CreateArcGeometry(center, radius, StartAngle, endAngle);
    }

    private static Geometry CreateArcGeometry(Point center, double radius, double startAngle, double endAngle)
    {
        Point startPoint = CalculatePoint(center, radius, startAngle);
        Point endPoint = CalculatePoint(center, radius, endAngle);
        double sweep = endAngle - startAngle;
        bool isLargeArc = Math.Abs(sweep) >= 180d;

        PathFigure figure = new()
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Size(radius, radius),
            RotationAngle = 0d,
            IsLargeArc = isLargeArc,
            SweepDirection = sweep >= 0d ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            IsStroked = true
        });

        return new PathGeometry([figure]);
    }

    private static Point CalculatePoint(Point center, double radius, double angle)
    {
        double radians = angle * Math.PI / 180d;
        double x = center.X + radius * Math.Cos(radians);
        double y = center.Y + radius * Math.Sin(radians);
        return new Point(x, y);
    }
}
