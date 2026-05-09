namespace Foundry.ViewModels
{
    public sealed partial class AboutUsSettingViewModel : ObservableObject
    {
        private readonly Foundry.Services.Localization.IApplicationLocalizationService localizationService;

        public AboutUsSettingViewModel(Foundry.Services.Localization.IApplicationLocalizationService localizationService)
        {
            this.localizationService = localizationService;
        }

        public string Title => localizationService.GetString("AboutDialog.Title");
        public string Subtitle => localizationService.GetString("AboutDialog.Subtitle");
        public string AppName => FoundryApplicationInfo.AppName;
        public string Version => FoundryApplicationInfo.Version;
        public string Description => localizationService.GetString("AboutDialog.Description");
        public string AboutSectionText => localizationService.GetString("AboutDialog.AboutSection");
        public string LicensesSectionText => localizationService.GetString("AboutDialog.LicensesSection");
        public string ContributorsSectionText => localizationService.GetString("AboutDialog.ContributorsSection");
        public string ReleaseNotesSectionText => localizationService.GetString("AboutDialog.ReleaseNotesSection");
        public string RepositoryText => localizationService.GetString("AboutDialog.RepositoryLink");
        public string SupportText => localizationService.GetString("AboutDialog.SupportLink");
        public string LicenseText => localizationService.GetString("AboutDialog.LicenseLink");
        public string AuthorsText => localizationService.GetString("AboutDialog.AuthorsText");
        public string FooterText => localizationService.GetString("AboutDialog.Footer");
        public string UsefulLinksText => localizationService.GetString("AboutDialog.UsefulLinks");
        public string CloseText => localizationService.GetString("Common.Close");
        public string ReleaseNotesLoadingText => localizationService.GetString("AboutDialog.ReleaseNotesLoading");
        public string ReleaseNotesErrorText => localizationService.GetString("AboutDialog.ReleaseNotesError");
        public string OpenReleaseNotesText => localizationService.GetString("AboutDialog.OpenReleaseNotes");
        public Uri RepositoryUri { get; } = new(FoundryApplicationInfo.RepositoryUrl);
        public Uri LatestReleaseUri { get; } = new(FoundryApplicationInfo.LatestReleaseUrl);
        public Uri ReleasesUri { get; } = new(FoundryApplicationInfo.ReleasesUrl);
        public Uri SupportUri { get; } = new(FoundryApplicationInfo.SupportUrl);
        public Uri LicenseUri { get; } = new(FoundryApplicationInfo.LicenseUrl);

        public IReadOnlyList<AboutLinkItemViewModel> LicenseItems => new[]
        {
            new AboutLinkItemViewModel(
                localizationService.GetString("AboutDialog.FoundryLicenseTitle"),
                localizationService.GetString("AboutDialog.FoundryLicenseDescription"),
                localizationService.GetString("AboutDialog.OpenLink"),
                LicenseUri),
            new AboutLinkItemViewModel(
                localizationService.GetString("AboutDialog.SevenZipLicenseTitle"),
                localizationService.GetString("AboutDialog.SevenZipLicenseDescription"),
                localizationService.GetString("AboutDialog.OpenLink"),
                new Uri("https://www.7-zip.org/license.txt")),
            new AboutLinkItemViewModel(
                localizationService.GetString("AboutDialog.ThirdPartyLicenseTitle"),
                localizationService.GetString("AboutDialog.ThirdPartyLicenseDescription"),
                localizationService.GetString("AboutDialog.OpenLink"),
                RepositoryUri)
        };

        public IReadOnlyList<ContributorItemViewModel> ContributorItems => new[]
        {
            new ContributorItemViewModel(
                "Mickaël CHAVE",
                localizationService.GetString("AboutDialog.ProjectAuthorRole"),
                "MC",
                localizationService.GetString("AboutDialog.OpenProfile"),
                new Uri("https://github.com/mchave3"),
                new Uri("https://github.com/mchave3.png"))
        };
    }

    public sealed record AboutLinkItemViewModel(string Title, string Description, string LinkText, Uri Uri);

    public sealed record ContributorItemViewModel(
        string Name,
        string Role,
        string Initials,
        string LinkText,
        Uri ProfileUri,
        Uri AvatarUri);
}
