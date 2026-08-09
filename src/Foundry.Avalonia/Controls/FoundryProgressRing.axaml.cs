// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Foundry.Avalonia.Services.Motion;

namespace Foundry.Avalonia.Controls;

[PseudoClasses(":motion-full")]
public class FoundryProgressRing : ProgressBar
{
    public static readonly StyledProperty<string?> AccessibleLabelProperty =
        AutomationProperties.NameProperty.AddOwner<FoundryProgressRing>();

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<FoundryProgressRing, double>(
            nameof(StrokeThickness),
            defaultValue: 8,
            validate: value => double.IsFinite(value) && value > 0);

    public static readonly DirectProperty<FoundryProgressRing, double> SweepAngleProperty =
        AvaloniaProperty.RegisterDirect<FoundryProgressRing, double>(
            nameof(SweepAngle),
            control => control.SweepAngle);

    public static readonly StyledProperty<FoundryMotionMode> MotionModeProperty =
        AvaloniaProperty.Register<FoundryProgressRing, FoundryMotionMode>(
            nameof(MotionMode),
            defaultValue: FoundryMotionMode.Reduced);

    private double _sweepAngle;

    public string? AccessibleLabel
    {
        get => GetValue(AccessibleLabelProperty);
        set => SetValue(AccessibleLabelProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double SweepAngle
    {
        get => _sweepAngle;
        private set => SetAndRaise(SweepAngleProperty, ref _sweepAngle, value);
    }

    public FoundryMotionMode MotionMode
    {
        get => GetValue(MotionModeProperty);
        set => SetValue(MotionModeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MotionModeProperty)
        {
            PseudoClasses.Set(
                ":motion-full",
                change.GetNewValue<FoundryMotionMode>() == FoundryMotionMode.Full);
        }

        if (change.Property == MinimumProperty ||
            change.Property == MaximumProperty ||
            change.Property == ValueProperty ||
            change.Property == IsIndeterminateProperty)
        {
            UpdateSweepAngle();
        }
    }

    private void UpdateSweepAngle()
    {
        if (IsIndeterminate)
        {
            SweepAngle = 90;
            return;
        }

        double range = Maximum - Minimum;
        double fraction = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);
        SweepAngle = fraction * 360;
    }
}
