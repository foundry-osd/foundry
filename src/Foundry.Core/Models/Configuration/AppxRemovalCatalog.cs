namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Provides the supported Windows 11 provisioned AppX removal catalog.
/// </summary>
public static class AppxRemovalCatalog
{
    private static readonly AppxRemovalCatalogEntry[] CatalogEntries =
    [
        Create("Clipchamp.Clipchamp", "Clipchamp", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.BingNews", "Microsoft News", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.BingSearch", "Bing Search", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.BingWeather", "MSN Weather", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.Copilot", "Microsoft Copilot", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.GetHelp", "Get Help", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.Getstarted", "Tips / Get Started", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.MicrosoftOfficeHub", "Microsoft 365 / Office Hub", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.PowerAutomateDesktop", "Power Automate", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.Todos", "Microsoft To Do", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.Windows.DevHome", "Dev Home", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.WindowsFeedbackHub", "Feedback Hub", "Consumer / Bloatware / Onboarding", true),
        Create("Microsoft.OutlookForWindows", "Outlook for Windows", "Microsoft 365 / Communication / Collaboration", false),
        Create("Microsoft.Office.OneNote", "OneNote for Windows 10", "Microsoft 365 / Communication / Collaboration", false),
        Create("microsoft.windowscommunicationsapps", "Mail and Calendar", "Microsoft 365 / Communication / Collaboration", false),
        Create("Microsoft.People", "People", "Microsoft 365 / Communication / Collaboration", false),
        Create("MicrosoftTeams", "Microsoft Teams (legacy / consumer)", "Microsoft 365 / Communication / Collaboration", false),
        Create("MSTeams", "Microsoft Teams", "Microsoft 365 / Communication / Collaboration", false),
        Create("Microsoft.M365Companions", "Microsoft 365 Companions", "Microsoft 365 / Communication / Collaboration", false),
        Create("Microsoft.YourPhone", "Phone Link", "Phone / Cross-Device", true),
        Create("MicrosoftWindows.CrossDevice", "Cross Device Experience Host", "Phone / Cross-Device", true),
        Create("Microsoft.Edge.GameAssist", "Microsoft Edge Game Assist", "Gaming / Xbox", true),
        Create("Microsoft.GamingApp", "Xbox", "Gaming / Xbox", true),
        Create("Microsoft.Xbox.TCUI", "Xbox TCUI", "Gaming / Xbox", true),
        Create("Microsoft.XboxApp", "Xbox Console Companion", "Gaming / Xbox", true),
        Create("Microsoft.XboxGameOverlay", "Xbox Game Overlay", "Gaming / Xbox", true),
        Create("Microsoft.XboxGamingOverlay", "Xbox Game Bar", "Gaming / Xbox", true),
        Create("Microsoft.XboxIdentityProvider", "Xbox Identity Provider", "Gaming / Xbox", true),
        Create("Microsoft.XboxSpeechToTextOverlay", "Xbox Speech to Text Overlay", "Gaming / Xbox", true),
        Create("Microsoft.549981C3F5F10", "Cortana", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("Microsoft.SkypeApp", "Skype", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("Microsoft.Microsoft3DViewer", "3D Viewer", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("Microsoft.MixedReality.Portal", "Mixed Reality Portal", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("Microsoft.MSPaint", "Paint 3D", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("Microsoft.Print3D", "Print 3D", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("Microsoft.WindowsMaps", "Windows Maps", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("Microsoft.ZuneVideo", "Movies & TV", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("Microsoft.Wallet", "Microsoft Wallet", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("Microsoft.OneConnect", "Mobile Plans", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("Microsoft.Messaging", "Messaging", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family", "Legacy / Discontinued / Old Inbox Apps", true),
        Create("Microsoft.MicrosoftStickyNotes", "Sticky Notes", "Utilities / Native Apps", false),
        Create("Microsoft.Paint", "Paint", "Utilities / Native Apps", false),
        Create("Microsoft.ScreenSketch", "Snipping Tool", "Utilities / Native Apps", false),
        Create("Microsoft.Windows.Photos", "Photos", "Utilities / Native Apps", false),
        Create("Microsoft.WindowsAlarms", "Clock", "Utilities / Native Apps", false),
        Create("Microsoft.WindowsCalculator", "Calculator", "Utilities / Native Apps", false),
        Create("Microsoft.WindowsCamera", "Camera", "Utilities / Native Apps", false),
        Create("Microsoft.WindowsNotepad", "Notepad", "Utilities / Native Apps", false),
        Create("Microsoft.WindowsSoundRecorder", "Sound Recorder", "Utilities / Native Apps", false),
        Create("Microsoft.WindowsTerminal", "Windows Terminal", "Utilities / Native Apps", false),
        Create("Microsoft.ZuneMusic", "Media Player", "Utilities / Native Apps", false),
        Create("MicrosoftCorporationII.QuickAssist", "Quick Assist", "Utilities / Native Apps", false),
        Create("Microsoft.News", "Microsoft News", "Microsoft First-Party / Optional", true),
        Create("Microsoft.Windows.AIHub", "Windows AI Hub", "Microsoft First-Party / Optional", true),
        Create("Microsoft.MicrosoftJournal", "Microsoft Journal", "Microsoft First-Party / Optional", false),
        Create("Microsoft.Whiteboard", "Microsoft Whiteboard", "Microsoft First-Party / Optional", false),
        Create("Microsoft.RemoteDesktop", "Microsoft Remote Desktop", "Microsoft First-Party / Optional", false),
        Create("Microsoft.NetworkSpeedTest", "Network Speed Test", "Microsoft First-Party / Optional", false),
        Create("Microsoft.Office.Sway", "Sway", "Microsoft First-Party / Optional", false),
        Create("Microsoft.MicrosoftPowerBIForWindows", "Power BI", "Microsoft First-Party / Optional", false),
        Create("Microsoft.PCManager", "Microsoft PC Manager", "Microsoft First-Party / Optional", true)
    ];

    /// <summary>
    /// Gets the supported catalog entries.
    /// </summary>
    public static IReadOnlyList<AppxRemovalCatalogEntry> Entries => CatalogEntries;

    /// <summary>
    /// Returns whether a provisioned AppX package identifier exists in the supported catalog.
    /// </summary>
    public static bool ContainsPackageName(string packageName)
    {
        return CatalogEntries.Any(entry => string.Equals(entry.PackageName, packageName, StringComparison.OrdinalIgnoreCase));
    }

    private static AppxRemovalCatalogEntry Create(
        string packageName,
        string displayName,
        string category,
        bool defaultSelected)
    {
        return new AppxRemovalCatalogEntry
        {
            PackageName = packageName,
            DisplayName = displayName,
            Category = category,
            DefaultSelected = defaultSelected
        };
    }
}
