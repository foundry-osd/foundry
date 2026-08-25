// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Foundry.Deploy.Motion;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Views;

public partial class DeploymentStatusView : UserControl
{
    private DeploymentSessionViewModel? _session;

    public DeploymentStatusView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachSession();
        AttachSession(e.NewValue as DeploymentSessionViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachSession(DataContext as DeploymentSessionViewModel);
        ScrollToActiveTimelineEntry();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachSession();
        TransitionAnimator.Clear(CentralStateHost, CentralStateScale);
    }

    private void AttachSession(DeploymentSessionViewModel? session)
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
        foreach (DeploymentTimelineEntryViewModel entry in _session.TimelineEntries)
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
        foreach (DeploymentTimelineEntryViewModel entry in _session.TimelineEntries)
        {
            entry.PropertyChanged -= OnTimelineEntryPropertyChanged;
        }

        _session = null;
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeploymentSessionViewModel.CurrentPage))
        {
            AnimateTerminalStateChange();
        }

        if (e.PropertyName is nameof(DeploymentSessionViewModel.CurrentPage)
            or nameof(DeploymentSessionViewModel.CurrentStepName)
            or nameof(DeploymentSessionViewModel.FailedStepName)
            or nameof(DeploymentSessionViewModel.CompletionInstructionText))
        {
            RaiseStatusLiveRegionChanged();
        }
    }

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
            foreach (DeploymentTimelineEntryViewModel entry in e.OldItems)
            {
                entry.PropertyChanged -= OnTimelineEntryPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DeploymentTimelineEntryViewModel entry in e.NewItems)
            {
                entry.PropertyChanged += OnTimelineEntryPropertyChanged;
            }
        }

        ScrollToActiveTimelineEntry();
    }

    private void OnTimelineEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeploymentTimelineEntryViewModel.IsActive))
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

        DeploymentTimelineEntryViewModel? activeEntry = _session.TimelineEntries.FirstOrDefault(entry => entry.IsActive);
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

    private void AnimateTerminalStateChange()
    {
        if (_session?.CurrentPage is DeploymentPage.Success or DeploymentPage.Error)
        {
            TransitionAnimator.FadeAndScale(CentralStateHost, CentralStateScale, 0.98);
        }
    }

    private void ViewTechnicalDetails_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DeploymentErrorDetailsDialog
        {
            DataContext = DataContext,
            Owner = Window.GetWindow(this)
        };
        _ = dialog.ShowDialog();
    }
}
