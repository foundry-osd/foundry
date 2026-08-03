// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

internal sealed class WinPeDismProgressForwarder : IProgress<WinPeDismProgress>
{
    private readonly IProgress<WinPeMountedImageCustomizationProgress> _progress;
    private readonly int _percent;
    private readonly string _status;
    private readonly int? _taskIndex;
    private readonly int? _taskCount;
    private readonly int? _itemIndex;
    private readonly int? _itemCount;
    private readonly WinPeCustomizationItemCategory _itemCategory;

    public WinPeDismProgressForwarder(
        IProgress<WinPeMountedImageCustomizationProgress> progress,
        int percent,
        string status,
        int? taskIndex = null,
        int? taskCount = null,
        int? itemIndex = null,
        int? itemCount = null,
        WinPeCustomizationItemCategory itemCategory = WinPeCustomizationItemCategory.None)
    {
        _progress = progress;
        _percent = percent;
        _status = status;
        _taskIndex = taskIndex;
        _taskCount = taskCount;
        _itemIndex = itemIndex;
        _itemCount = itemCount;
        _itemCategory = itemCategory;
    }

    public void Report(WinPeDismProgress value)
    {
        _progress.Report(new WinPeMountedImageCustomizationProgress
        {
            Percent = _percent,
            Status = _status,
            DetailPercent = value.Percent,
            DetailStatus = value.Status,
            TaskIndex = _taskIndex,
            TaskCount = _taskCount,
            ItemIndex = value.ItemIndex ?? _itemIndex,
            ItemCount = value.ItemCount ?? _itemCount,
            ItemCategory = _itemCategory
        });
    }
}
