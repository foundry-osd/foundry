// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Foundry.Avalonia.Controls;
using Foundry.Avalonia.Services.Motion;

namespace Foundry.Connect.Views;

[PseudoClasses(":motion-full")]
public partial class ReadinessStatusView : UserControl
{
    public static readonly StyledProperty<FoundryMotionMode> MotionModeProperty =
        AvaloniaProperty.Register<ReadinessStatusView, FoundryMotionMode>(
            nameof(MotionMode),
            defaultValue: FoundryMotionMode.Reduced);

    private StatusIndicator? _readyStatus;
    private StatusIndicator? _waitingStatus;

    public ReadinessStatusView()
    {
        AvaloniaXamlLoader.Load(this);
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
            PseudoClasses.Set(":motion-full", change.GetNewValue<FoundryMotionMode>() == FoundryMotionMode.Full);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => (_readyStatus?.IsVisible == true ? _readyStatus : _waitingStatus)?.Focus());
    }

    private void OnStatusAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var status = (StatusIndicator)sender!;
        if (status.Name == "ReadyStatus")
        {
            _readyStatus = status;
        }
        else
        {
            _waitingStatus = status;
        }
    }
}
