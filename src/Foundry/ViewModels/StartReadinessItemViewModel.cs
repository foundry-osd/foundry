namespace Foundry.ViewModels;

public enum StartReadinessNavigationTarget
{
    None,
    Adk,
    General,
    Network,
    Autopilot,
    Customization
}

public sealed class StartReadinessItemViewModel
{
    public StartReadinessItemViewModel(
        string title,
        string description,
        string status,
        string glyph,
        bool expandsGroup,
        StartReadinessNavigationTarget navigationTarget = StartReadinessNavigationTarget.None,
        string actionText = "")
    {
        Title = title;
        Description = description;
        Status = status;
        Glyph = glyph;
        ExpandsGroup = expandsGroup;
        NavigationTarget = navigationTarget;
        ActionText = actionText;
    }

    public string Title { get; }

    public string Description { get; }

    public string Status { get; }

    public string Glyph { get; }

    public bool ExpandsGroup { get; }

    public StartReadinessNavigationTarget NavigationTarget { get; }

    public string ActionText { get; }

    public Visibility ActionVisibility => NavigationTarget == StartReadinessNavigationTarget.None
        ? Visibility.Collapsed
        : Visibility.Visible;
}
