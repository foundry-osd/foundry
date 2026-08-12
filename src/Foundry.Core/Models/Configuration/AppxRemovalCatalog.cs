// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Provides the supported Windows 11 provisioned AppX removal catalog.
/// </summary>
public static class AppxRemovalCatalog
{
    private static readonly AppxRemovalCatalogEntry[] CatalogEntries =
    [
        Create("Clipchamp.Clipchamp", "Clipchamp", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.BingNews", "Microsoft News", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.BingSearch", "Bing Search", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.BingWeather", "MSN Weather", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.GetHelp", "Get Help", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.Getstarted", "Tips / Get Started", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.MicrosoftOfficeHub", "Microsoft 365 / Office Hub", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.PowerAutomateDesktop", "Power Automate", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.Todos", "Microsoft To Do", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.Windows.DevHome", "Dev Home", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.WindowsFeedbackHub", "Feedback Hub", "Consumer / Bloatware / Onboarding"),
        Create("Microsoft.OutlookForWindows", "Outlook for Windows", "Microsoft 365 / Communication / Collaboration"),
        Create("Microsoft.Office.OneNote", "OneNote for Windows 10", "Microsoft 365 / Communication / Collaboration"),
        Create("microsoft.windowscommunicationsapps", "Mail and Calendar", "Microsoft 365 / Communication / Collaboration"),
        Create("Microsoft.People", "People", "Microsoft 365 / Communication / Collaboration"),
        Create("MicrosoftTeams", "Microsoft Teams (legacy / consumer)", "Microsoft 365 / Communication / Collaboration"),
        Create("MSTeams", "Microsoft Teams", "Microsoft 365 / Communication / Collaboration"),
        Create("Microsoft.M365Companions", "Microsoft 365 Companions", "Microsoft 365 / Communication / Collaboration"),
        Create("Microsoft.YourPhone", "Phone Link", "Phone / Cross-Device"),
        Create("MicrosoftWindows.CrossDevice", "Cross Device Experience Host", "Phone / Cross-Device"),
        Create("Microsoft.Edge.GameAssist", "Microsoft Edge Game Assist", "Gaming / Xbox"),
        Create("Microsoft.GamingApp", "Xbox", "Gaming / Xbox"),
        Create("Microsoft.Xbox.TCUI", "Xbox TCUI", "Gaming / Xbox"),
        Create("Microsoft.XboxApp", "Xbox Console Companion", "Gaming / Xbox"),
        Create("Microsoft.XboxGameOverlay", "Xbox Game Overlay", "Gaming / Xbox"),
        Create("Microsoft.XboxGamingOverlay", "Xbox Game Bar", "Gaming / Xbox"),
        Create("Microsoft.XboxIdentityProvider", "Xbox Identity Provider", "Gaming / Xbox"),
        Create("Microsoft.XboxSpeechToTextOverlay", "Xbox Speech to Text Overlay", "Gaming / Xbox"),
        Create("Microsoft.549981C3F5F10", "Cortana", "Legacy / Discontinued / Old Inbox Apps"),
        Create("Microsoft.SkypeApp", "Skype", "Legacy / Discontinued / Old Inbox Apps"),
        Create("Microsoft.Microsoft3DViewer", "3D Viewer", "Legacy / Discontinued / Old Inbox Apps"),
        Create("Microsoft.MixedReality.Portal", "Mixed Reality Portal", "Legacy / Discontinued / Old Inbox Apps"),
        Create("Microsoft.MSPaint", "Paint 3D", "Legacy / Discontinued / Old Inbox Apps"),
        Create("Microsoft.Print3D", "Print 3D", "Legacy / Discontinued / Old Inbox Apps"),
        Create("Microsoft.WindowsMaps", "Windows Maps", "Legacy / Discontinued / Old Inbox Apps"),
        Create("Microsoft.ZuneVideo", "Movies & TV", "Legacy / Discontinued / Old Inbox Apps"),
        Create("Microsoft.Wallet", "Microsoft Wallet", "Legacy / Discontinued / Old Inbox Apps"),
        Create("Microsoft.OneConnect", "Mobile Plans", "Legacy / Discontinued / Old Inbox Apps"),
        Create("Microsoft.Messaging", "Messaging", "Legacy / Discontinued / Old Inbox Apps"),
        Create("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family", "Legacy / Discontinued / Old Inbox Apps"),
        Create("Microsoft.MicrosoftStickyNotes", "Sticky Notes", "Utilities / Native Apps"),
        Create("Microsoft.Paint", "Paint", "Utilities / Native Apps"),
        Create("Microsoft.ScreenSketch", "Snipping Tool", "Utilities / Native Apps"),
        Create("Microsoft.Windows.Photos", "Photos", "Utilities / Native Apps"),
        Create("Microsoft.WindowsAlarms", "Clock", "Utilities / Native Apps"),
        Create("Microsoft.WindowsCalculator", "Calculator", "Utilities / Native Apps"),
        Create("Microsoft.WindowsCamera", "Camera", "Utilities / Native Apps"),
        Create("Microsoft.WindowsNotepad", "Notepad", "Utilities / Native Apps"),
        Create("Microsoft.WindowsSoundRecorder", "Sound Recorder", "Utilities / Native Apps"),
        Create("Microsoft.WindowsTerminal", "Windows Terminal", "Utilities / Native Apps"),
        Create("Microsoft.ZuneMusic", "Media Player", "Utilities / Native Apps"),
        Create("MicrosoftCorporationII.QuickAssist", "Quick Assist", "Utilities / Native Apps"),
        Create("Microsoft.News", "Microsoft News", "Microsoft First-Party / Optional"),
        Create("Microsoft.MicrosoftJournal", "Microsoft Journal", "Microsoft First-Party / Optional"),
        Create("Microsoft.Whiteboard", "Microsoft Whiteboard", "Microsoft First-Party / Optional"),
        Create("Microsoft.RemoteDesktop", "Microsoft Remote Desktop", "Microsoft First-Party / Optional"),
        Create("Microsoft.NetworkSpeedTest", "Network Speed Test", "Microsoft First-Party / Optional"),
        Create("Microsoft.Office.Sway", "Sway", "Microsoft First-Party / Optional"),
        Create("Microsoft.MicrosoftPowerBIForWindows", "Power BI", "Microsoft First-Party / Optional"),
        Create("Microsoft.PCManager", "Microsoft PC Manager", "Microsoft First-Party / Optional")
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
        string category)
    {
        return new AppxRemovalCatalogEntry
        {
            PackageName = packageName,
            DisplayName = displayName,
            Category = category
        };
    }
}
