// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Foundry.Wpf.Motion;

namespace Foundry.Wpf.Progress;

/// <summary>
/// Shared progress page of a run: session panel, timeline, central progress or terminal state, step counter and cancel action.
/// Its data context is an <see cref="IProgressPageViewModel"/>; the host opens its own technical details from
/// <see cref="TechnicalDetailsRequested"/>.
/// </summary>
public partial class ProgressPageView : UserControl
{
    /// <summary>Identifies the bubbling <see cref="TechnicalDetailsRequested"/> routed event.</summary>
    public static readonly RoutedEvent TechnicalDetailsRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(TechnicalDetailsRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ProgressPageView));

    private IProgressPageViewModel? _session;

    public ProgressPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Occurs when the operator asks for the technical details of a failure.</summary>
    public event RoutedEventHandler TechnicalDetailsRequested
    {
        add => AddHandler(TechnicalDetailsRequestedEvent, value);
        remove => RemoveHandler(TechnicalDetailsRequestedEvent, value);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachSession(e.NewValue as IProgressPageViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachSession(DataContext as IProgressPageViewModel);
        ScrollToActiveTimelineEntry();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachSession();
        TransitionAnimator.Clear(CentralStateHost, CentralStateScale);
    }

    private void AttachSession(IProgressPageViewModel? session)
    {
        if (_session == session)
        {
            return;
        }

        DetachSession();
        _session = session;
        if (_session is null)
        {
            return;
        }

        _session.PropertyChanged += OnSessionPropertyChanged;
        _session.TimelineEntries.CollectionChanged += OnTimelineCollectionChanged;
        foreach (ProgressTimelineEntry entry in _session.TimelineEntries)
        {
            entry.PropertyChanged += OnTimelineEntryPropertyChanged;
        }
    }

    private void DetachSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.PropertyChanged -= OnSessionPropertyChanged;
        _session.TimelineEntries.CollectionChanged -= OnTimelineCollectionChanged;
        foreach (ProgressTimelineEntry entry in _session.TimelineEntries)
        {
            entry.PropertyChanged -= OnTimelineEntryPropertyChanged;
        }

        _session = null;
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A page change raises every page flag; only the flag raised as true marks the transition, so each transition
        // animates and announces once.
        bool isPageShown = IsPageShown(e.PropertyName);
        if (isPageShown)
        {
            AnimateTerminalStateChange(e.PropertyName);
        }

        if (isPageShown
            || e.PropertyName is nameof(IProgressPageViewModel.CurrentStepName)
            or nameof(IProgressPageViewModel.TerminalErrorTitle)
            or nameof(IProgressPageViewModel.CompletionInstructionText))
        {
            RaiseStatusLiveRegionChanged();
        }
    }

    private bool IsPageShown(string? propertyName) => propertyName switch
    {
        nameof(IProgressPageViewModel.IsProgressPage) => _session?.IsProgressPage == true,
        nameof(IProgressPageViewModel.IsSuccessPage) => _session?.IsSuccessPage == true,
        nameof(IProgressPageViewModel.IsErrorPage) => _session?.IsErrorPage == true,
        _ => false
    };

    private void RaiseStatusLiveRegionChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded)
            {
                return;
            }

            AutomationPeer? peer = UIElementAutomationPeer.FromElement(CentralStateHost) ??
                                   UIElementAutomationPeer.CreatePeerForElement(CentralStateHost);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        });
    }

    private void OnTimelineCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ProgressTimelineEntry entry in e.OldItems)
            {
                entry.PropertyChanged -= OnTimelineEntryPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ProgressTimelineEntry entry in e.NewItems)
            {
                entry.PropertyChanged += OnTimelineEntryPropertyChanged;
            }
        }

        ScrollToActiveTimelineEntry();
    }

    private void OnTimelineEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProgressTimelineEntry.IsActive))
        {
            ScrollToActiveTimelineEntry();
        }
    }

    private void ScrollToActiveTimelineEntry()
    {
        if (_session is null)
        {
            return;
        }

        ProgressTimelineEntry? activeEntry = _session.TimelineEntries.FirstOrDefault(entry => entry.IsActive);
        if (activeEntry is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (TimelineItems.ItemContainerGenerator.ContainerFromItem(activeEntry) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        });
    }

    private void AnimateTerminalStateChange(string? shownPage)
    {
        if (shownPage is nameof(IProgressPageViewModel.IsSuccessPage) or nameof(IProgressPageViewModel.IsErrorPage))
        {
            TransitionAnimator.FadeAndScale(CentralStateHost, CentralStateScale, 0.98);
        }
    }

    private void ViewTechnicalDetails_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(TechnicalDetailsRequestedEvent, this));
    }
}
