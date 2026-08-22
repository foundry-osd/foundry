// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Wizard;

public sealed class DeploymentWizardNavigationState
{
    private IReadOnlyList<DeploymentWizardStepDefinition> _steps;
    private int _currentIndex;
    private int _furthestIndex;

    public DeploymentWizardNavigationState(IReadOnlyList<DeploymentWizardStepDefinition> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            throw new ArgumentException("At least one wizard step is required.", nameof(steps));
        }

        _steps = steps;
    }

    public DeploymentWizardStepId CurrentStepId => _steps[_currentIndex].Id;

    public bool IsReturningToSummary { get; private set; }

    public bool CanNavigateTo(DeploymentWizardStepId stepId)
    {
        int targetIndex = IndexOf(stepId);
        return targetIndex >= 0 && targetIndex <= _furthestIndex;
    }

    public bool TryNavigateTo(DeploymentWizardStepId stepId)
    {
        if (!CanNavigateTo(stepId))
        {
            return false;
        }

        _currentIndex = IndexOf(stepId);
        IsReturningToSummary = false;
        return true;
    }

    public bool BeginSummaryEdit(DeploymentWizardStepId stepId)
    {
        int summaryIndex = IndexOf(DeploymentWizardStepId.Summary);
        int targetIndex = IndexOf(stepId);
        if (_currentIndex != summaryIndex || targetIndex < 0 || targetIndex >= summaryIndex)
        {
            return false;
        }

        _currentIndex = targetIndex;
        IsReturningToSummary = true;
        return true;
    }

    public bool Advance()
    {
        if (IsReturningToSummary)
        {
            _currentIndex = IndexOf(DeploymentWizardStepId.Summary);
            _furthestIndex = Math.Max(_furthestIndex, _currentIndex);
            IsReturningToSummary = false;
            return true;
        }

        if (_currentIndex >= _steps.Count - 1)
        {
            return false;
        }

        _currentIndex++;
        _furthestIndex = Math.Max(_furthestIndex, _currentIndex);
        return true;
    }

    public bool MovePrevious()
    {
        if (_currentIndex == 0)
        {
            return false;
        }

        _currentIndex--;
        IsReturningToSummary = false;
        return true;
    }

    public void ReplaceSteps(IReadOnlyList<DeploymentWizardStepDefinition> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            throw new ArgumentException("At least one wizard step is required.", nameof(steps));
        }

        DeploymentWizardStepId currentStepId = CurrentStepId;
        DeploymentWizardStepId furthestStepId = _steps[_furthestIndex].Id;
        _steps = steps;
        _currentIndex = Math.Max(0, IndexOf(currentStepId));
        _furthestIndex = Math.Max(_currentIndex, IndexOf(furthestStepId));
        IsReturningToSummary = false;
    }

    private int IndexOf(DeploymentWizardStepId stepId)
    {
        for (int index = 0; index < _steps.Count; index++)
        {
            if (_steps[index].Id == stepId)
            {
                return index;
            }
        }

        return -1;
    }
}
