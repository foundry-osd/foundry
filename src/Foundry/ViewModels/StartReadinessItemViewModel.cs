namespace Foundry.ViewModels;

/// <summary>
/// Identifies the navigation target opened by a readiness item action.
/// </summary>
public enum StartReadinessNavigationTarget
{
    /// <summary>
    /// No navigation action is available.
    /// </summary>
    None,

    /// <summary>
    /// Navigate to the ADK readiness page.
    /// </summary>
    Adk,

    /// <summary>
    /// Navigate to general media configuration.
    /// </summary>
    General,

    /// <summary>
    /// Navigate to network configuration.
    /// </summary>
    Network,

    /// <summary>
    /// Navigate to Autopilot configuration.
    /// </summary>
    Autopilot,

    /// <summary>
    /// Navigate to deployment customization settings.
    /// </summary>
    Customization
}

/// <summary>
/// Represents one readiness row shown on the media start page.
/// </summary>
public sealed class StartReadinessItemViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StartReadinessItemViewModel"/> class.
    /// </summary>
    /// <param name="title">Readiness item title.</param>
    /// <param name="description">Readiness item description.</param>
    /// <param name="status">Current readiness status text.</param>
    /// <param name="glyph">Icon glyph shown beside the item.</param>
    /// <param name="expandsGroup">Whether activating the item expands a local readiness group.</param>
    /// <param name="navigationTarget">Optional shell navigation target.</param>
    /// <param name="actionText">Optional action button text.</param>
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

    /// <summary>
    /// Gets the readiness item title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the readiness item description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the current readiness status text.
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// Gets the icon glyph shown beside the item.
    /// </summary>
    public string Glyph { get; }

    /// <summary>
    /// Gets a value indicating whether the item expands a readiness group instead of navigating.
    /// </summary>
    public bool ExpandsGroup { get; }

    /// <summary>
    /// Gets the target page opened by the action button.
    /// </summary>
    public StartReadinessNavigationTarget NavigationTarget { get; }

    public string ActionText { get; }

    /// <summary>
    /// Gets the action button visibility derived from <see cref="NavigationTarget"/>.
    /// </summary>
    public Visibility ActionVisibility => NavigationTarget == StartReadinessNavigationTarget.None
        ? Visibility.Collapsed
        : Visibility.Visible;
}
