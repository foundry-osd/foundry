namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Provides the supported Windows 11 provisioned AppX removal catalog.
/// </summary>
public static class AppxRemovalCatalog
{
    private static readonly AppxRemovalCatalogEntry[] CatalogEntries =
    [
        Create("Clipchamp.Clipchamp", "Clipchamp", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.BingNews", "Microsoft News", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.BingSearch", "Bing Search", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.BingWeather", "MSN Weather", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.Copilot", "Microsoft Copilot", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.GetHelp", "Get Help", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.Getstarted", "Tips / Get Started", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.MicrosoftOfficeHub", "Microsoft 365 / Office Hub", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.PowerAutomateDesktop", "Power Automate", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.Todos", "Microsoft To Do", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.Windows.DevHome", "Dev Home", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.WindowsFeedbackHub", "Feedback Hub", "Consumer / bloatware / onboarding", true),
        Create("Microsoft.OutlookForWindows", "Outlook for Windows", "Microsoft 365 / communication / collaboration", false),
        Create("Microsoft.Office.OneNote", "OneNote for Windows 10", "Microsoft 365 / communication / collaboration", false),
        Create("microsoft.windowscommunicationsapps", "Mail and Calendar", "Microsoft 365 / communication / collaboration", false),
        Create("Microsoft.People", "People", "Microsoft 365 / communication / collaboration", false),
        Create("MicrosoftTeams", "Microsoft Teams (legacy / consumer)", "Microsoft 365 / communication / collaboration", false),
        Create("MSTeams", "Microsoft Teams", "Microsoft 365 / communication / collaboration", false),
        Create("Microsoft.M365Companions", "Microsoft 365 Companions", "Microsoft 365 / communication / collaboration", false),
        Create("Microsoft.YourPhone", "Phone Link", "Phone / cross-device", true),
        Create("MicrosoftWindows.CrossDevice", "Cross Device Experience Host", "Phone / cross-device", true),
        Create("Microsoft.Edge.GameAssist", "Microsoft Edge Game Assist", "Gaming / Xbox", true),
        Create("Microsoft.GamingApp", "Xbox", "Gaming / Xbox", true),
        Create("Microsoft.Xbox.TCUI", "Xbox TCUI", "Gaming / Xbox", true),
        Create("Microsoft.XboxApp", "Xbox Console Companion", "Gaming / Xbox", true),
        Create("Microsoft.XboxGameOverlay", "Xbox Game Overlay", "Gaming / Xbox", true),
        Create("Microsoft.XboxGamingOverlay", "Xbox Game Bar", "Gaming / Xbox", true),
        Create("Microsoft.XboxIdentityProvider", "Xbox Identity Provider", "Gaming / Xbox", true),
        Create("Microsoft.XboxSpeechToTextOverlay", "Xbox Speech to Text Overlay", "Gaming / Xbox", true),
        Create("Microsoft.549981C3F5F10", "Cortana", "Legacy / discontinued / old inbox apps", true),
        Create("Microsoft.SkypeApp", "Skype", "Legacy / discontinued / old inbox apps", true),
        Create("Microsoft.Microsoft3DViewer", "3D Viewer", "Legacy / discontinued / old inbox apps", true),
        Create("Microsoft.MixedReality.Portal", "Mixed Reality Portal", "Legacy / discontinued / old inbox apps", true),
        Create("Microsoft.MSPaint", "Paint 3D", "Legacy / discontinued / old inbox apps", true),
        Create("Microsoft.Print3D", "Print 3D", "Legacy / discontinued / old inbox apps", true),
        Create("Microsoft.WindowsMaps", "Windows Maps", "Legacy / discontinued / old inbox apps", true),
        Create("Microsoft.ZuneVideo", "Movies & TV", "Legacy / discontinued / old inbox apps", true),
        Create("Microsoft.Wallet", "Microsoft Wallet", "Legacy / discontinued / old inbox apps", true),
        Create("Microsoft.OneConnect", "Mobile Plans", "Legacy / discontinued / old inbox apps", true),
        Create("Microsoft.Messaging", "Messaging", "Legacy / discontinued / old inbox apps", true),
        Create("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family", "Legacy / discontinued / old inbox apps", true),
        Create("Microsoft.MicrosoftStickyNotes", "Sticky Notes", "Utilities / native apps", false),
        Create("Microsoft.Paint", "Paint", "Utilities / native apps", false),
        Create("Microsoft.ScreenSketch", "Snipping Tool", "Utilities / native apps", false),
        Create("Microsoft.Windows.Photos", "Photos", "Utilities / native apps", false),
        Create("Microsoft.WindowsAlarms", "Clock", "Utilities / native apps", false),
        Create("Microsoft.WindowsCalculator", "Calculator", "Utilities / native apps", false),
        Create("Microsoft.WindowsCamera", "Camera", "Utilities / native apps", false),
        Create("Microsoft.WindowsNotepad", "Notepad", "Utilities / native apps", false),
        Create("Microsoft.WindowsSoundRecorder", "Sound Recorder", "Utilities / native apps", false),
        Create("Microsoft.WindowsTerminal", "Windows Terminal", "Utilities / native apps", false),
        Create("Microsoft.ZuneMusic", "Media Player", "Utilities / native apps", false),
        Create("MicrosoftCorporationII.QuickAssist", "Quick Assist", "Utilities / native apps", false),
        Create("Microsoft.News", "Microsoft News", "Microsoft first-party / optional", true),
        Create("Microsoft.Windows.AIHub", "Windows AI Hub", "Microsoft first-party / optional", true),
        Create("Microsoft.MicrosoftJournal", "Microsoft Journal", "Microsoft first-party / optional", false),
        Create("Microsoft.Whiteboard", "Microsoft Whiteboard", "Microsoft first-party / optional", false),
        Create("Microsoft.RemoteDesktop", "Microsoft Remote Desktop", "Microsoft first-party / optional", false),
        Create("Microsoft.NetworkSpeedTest", "Network Speed Test", "Microsoft first-party / optional", false),
        Create("Microsoft.Office.Sway", "Sway", "Microsoft first-party / optional", false),
        Create("Microsoft.MicrosoftPowerBIForWindows", "Power BI", "Microsoft first-party / optional", false),
        Create("Microsoft.PCManager", "Microsoft PC Manager", "Microsoft first-party / optional", true)
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
