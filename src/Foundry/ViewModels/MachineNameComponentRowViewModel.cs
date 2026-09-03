// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Core.Models.Configuration;
using Microsoft.UI.Xaml;

namespace Foundry.ViewModels;

public sealed partial class MachineNameComponentRowViewModel : ObservableObject
{
    private readonly Action _changed;
    private string _displayName;
    private string _staticText;
    private double _maximumLength;
    private int _truncationIndex;
    private bool _canMoveUp;
    private bool _canMoveDown;

    public MachineNameComponentRowViewModel(
        MachineNameComponentSettings settings,
        string displayName,
        Action changed)
    {
        Type = settings.Type;
        _displayName = displayName;
        _staticText = settings.StaticText ?? string.Empty;
        _maximumLength = settings.MaximumLength ?? DefaultMaximumLength(settings.Type);
        _truncationIndex = settings.Truncation == MachineNameTruncation.KeepRight ? 1 : 0;
        _changed = changed;
    }

    public MachineNameComponentType Type { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public string StaticText
    {
        get => _staticText;
        set
        {
            if (SetProperty(ref _staticText, value))
            {
                _changed();
            }
        }
    }

    public double MaximumLength
    {
        get => _maximumLength;
        set
        {
            double normalized = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 1, 15) : 1;
            if (SetProperty(ref _maximumLength, normalized))
            {
                _changed();
            }
        }
    }

    public int TruncationIndex
    {
        get => _truncationIndex;
        set
        {
            if (SetProperty(ref _truncationIndex, value))
            {
                _changed();
            }
        }
    }

    public Visibility StaticTextVisibility => Type == MachineNameComponentType.StaticText
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility MaximumLengthVisibility => Type == MachineNameComponentType.StaticText
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility TruncationVisibility => Type is not MachineNameComponentType.StaticText and not MachineNameComponentType.Random
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanMoveUp
    {
        get => _canMoveUp;
        private set => SetProperty(ref _canMoveUp, value);
    }

    public bool CanMoveDown
    {
        get => _canMoveDown;
        private set => SetProperty(ref _canMoveDown, value);
    }

    public MachineNameComponentSettings BuildSettings() => new()
    {
        Type = Type,
        StaticText = Type == MachineNameComponentType.StaticText ? StaticText : null,
        MaximumLength = Type == MachineNameComponentType.StaticText ? null : (int)MaximumLength,
        Truncation = Type is MachineNameComponentType.StaticText or MachineNameComponentType.Random
            ? null
            : TruncationIndex == 1 ? MachineNameTruncation.KeepRight : MachineNameTruncation.KeepLeft
    };

    public void RefreshDisplayName(string displayName)
    {
        DisplayName = displayName;
    }

    public void UpdatePosition(int index, int count)
    {
        CanMoveUp = index > 0;
        CanMoveDown = index >= 0 && index < count - 1;
    }

    private static int DefaultMaximumLength(MachineNameComponentType type) => type == MachineNameComponentType.Random ? 6 : 15;
}
