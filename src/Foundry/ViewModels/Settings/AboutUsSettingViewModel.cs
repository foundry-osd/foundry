using System.Collections.ObjectModel;
using Foundry.Core.Services.Application;
using Foundry.Services.GitHub;

namespace Foundry.ViewModels
{
    public sealed partial class AboutUsSettingViewModel : ObservableObject
    {
        private readonly Foundry.Services.Localization.IApplicationLocalizationService localizationService;
        private readonly IGitHubRepositoryContributorService contributorService;
        private readonly IAppDispatcher appDispatcher;
        private readonly IExternalProcessLauncher externalProcessLauncher;
        private bool hasLoadedContributors;

        public AboutUsSettingViewModel(
            Foundry.Services.Localization.IApplicationLocalizationService localizationService,
            IGitHubRepositoryContributorService contributorService,
            IAppDispatcher appDispatcher,
            IExternalProcessLauncher externalProcessLauncher)
        {
            this.localizationService = localizationService;
            this.contributorService = contributorService;
            this.appDispatcher = appDispatcher;
            this.externalProcessLauncher = externalProcessLauncher;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ContributorsLoadingVisibility))]
        [NotifyPropertyChangedFor(nameof(ContributorsListVisibility))]
        [NotifyPropertyChangedFor(nameof(ContributorsErrorVisibility))]
        public partial bool IsLoadingContributors { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ContributorsListVisibility))]
        [NotifyPropertyChangedFor(nameof(ContributorsErrorVisibility))]
        public partial bool HasContributorLoadFailed { get; set; }

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
        public string FoundryLicenseTitle => localizationService.GetString("AboutDialog.FoundryLicenseTitle");
        public string FoundryLicenseDescription => localizationService.GetString("AboutDialog.FoundryLicenseDescription");
        public string ThirdPartyLicenseTitle => localizationService.GetString("AboutDialog.ThirdPartyLicenseTitle");
        public string ThirdPartyLicenseDescription => localizationService.GetString("AboutDialog.ThirdPartyLicenseDescription");
        public string CloseText => localizationService.GetString("Common.Close");
        public string ReleaseNotesLoadingText => localizationService.GetString("AboutDialog.ReleaseNotesLoading");
        public string ReleaseNotesErrorText => localizationService.GetString("AboutDialog.ReleaseNotesError");
        public string OpenReleaseNotesText => localizationService.GetString("AboutDialog.OpenReleaseNotes");
        public string ContributorsLoadingText => localizationService.GetString("AboutDialog.ContributorsLoading");
        public string ContributorsErrorText => localizationService.GetString("AboutDialog.ContributorsError");
        public Uri RepositoryUri { get; } = new(FoundryApplicationInfo.RepositoryUrl);
        public Uri LatestReleaseUri { get; } = new(FoundryApplicationInfo.LatestReleaseUrl);
        public Uri ReleasesUri { get; } = new(FoundryApplicationInfo.ReleasesUrl);
        public Uri SupportUri { get; } = new(FoundryApplicationInfo.SupportUrl);
        public Uri LicenseUri { get; } = new(FoundryApplicationInfo.LicenseUrl);

        public IReadOnlyList<ThirdPartyLicenseItemViewModel> ThirdPartyLicenseItems => new[]
        {
            CreateThirdPartyLicenseItem(
                "7-Zip Extra",
                "LGPL / BSD",
                "https://www.7-zip.org/license.txt",
                "https://www.7-zip.org/",
                false),
            CreateThirdPartyLicenseItem(
                ".NET",
                "MIT",
                "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT",
                "https://dotnet.microsoft.com/",
                true),
            CreateThirdPartyLicenseItem(
                "Azure.Identity",
                "MIT",
                "https://github.com/Azure/azure-sdk-for-net/blob/main/LICENSE.txt",
                "https://github.com/Azure/azure-sdk-for-net",
                false),
            CreateThirdPartyLicenseItem(
                "CommunityToolkit.Mvvm",
                "MIT",
                "https://github.com/CommunityToolkit/dotnet/blob/main/License.md",
                "https://github.com/CommunityToolkit/dotnet",
                true),
            CreateThirdPartyLicenseItem(
                "CommunityToolkit.WinUI SettingsControls",
                "MIT",
                "https://github.com/CommunityToolkit/Windows/blob/main/License.md",
                "https://github.com/CommunityToolkit/Windows",
                false),
            CreateThirdPartyLicenseItem(
                "DevWinUI controls and tooling",
                "MIT",
                "https://github.com/Ghost1372/DevWinUI/blob/main/LICENSE",
                "https://github.com/Ghost1372/DevWinUI",
                true),
            CreateThirdPartyLicenseItem(
                "HtmlAgilityPack",
                "MIT",
                "https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE",
                "https://html-agility-pack.net/",
                false),
            CreateThirdPartyLicenseItem(
                "Microsoft Windows SDK Build Tools",
                "Microsoft",
                "https://aka.ms/WindowSDKLicenseTerms",
                "https://developer.microsoft.com/windows/downloads/windows-sdk/",
                true),
            CreateThirdPartyLicenseItem(
                "Microsoft.Extensions.Hosting / DependencyInjection",
                "MIT",
                "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT",
                "https://learn.microsoft.com/dotnet/core/extensions/generic-host",
                false),
            CreateThirdPartyLicenseItem(
                "Microsoft.Extensions.Logging.Abstractions",
                "MIT",
                "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT",
                "https://learn.microsoft.com/dotnet/core/extensions/logging",
                true),
            CreateThirdPartyLicenseItem(
                "Microsoft.Windows.CsWinRT",
                "MIT",
                "https://github.com/microsoft/CsWinRT/blob/master/LICENSE",
                "https://github.com/microsoft/CsWinRT",
                false),
            CreateThirdPartyLicenseItem(
                "Serilog core and sinks",
                "Apache-2.0",
                "https://github.com/serilog/serilog/blob/dev/LICENSE",
                "https://serilog.net/",
                true),
            CreateThirdPartyLicenseItem(
                "Serilog.Extensions.Logging",
                "Apache-2.0",
                "https://github.com/serilog/serilog-extensions-logging/blob/dev/LICENSE",
                "https://github.com/serilog/serilog-extensions-logging",
                false),
            CreateThirdPartyLicenseItem(
                "Velopack",
                "MIT",
                "https://github.com/velopack/velopack/blob/develop/LICENSE",
                "https://velopack.io/",
                true),
            CreateThirdPartyLicenseItem(
                "Windows ADK / Windows PE",
                "Microsoft",
                "https://learn.microsoft.com/windows-hardware/get-started/adk-install",
                "https://learn.microsoft.com/windows-hardware/get-started/adk-install",
                false),
            CreateThirdPartyLicenseItem(
                "Windows App SDK / WinUI 3",
                "MIT",
                "https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE",
                "https://github.com/microsoft/WindowsAppSDK",
                true),
            CreateThirdPartyLicenseItem(
                "WinUI.TableView",
                "MIT",
                "https://github.com/w-ahmad/WinUI.TableView/blob/master/LICENSE.md",
                "https://github.com/w-ahmad/WinUI.TableView",
                false)
        };

        public ObservableCollection<ContributorItemViewModel> ContributorItems { get; } = [];

        public Visibility ContributorsLoadingVisibility => IsLoadingContributors ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ContributorsListVisibility => !IsLoadingContributors && !HasContributorLoadFailed && ContributorItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility ContributorsErrorVisibility => !IsLoadingContributors && HasContributorLoadFailed
            ? Visibility.Visible
            : Visibility.Collapsed;

        public async Task LoadContributorsAsync(CancellationToken cancellationToken = default)
        {
            if (hasLoadedContributors || IsLoadingContributors)
            {
                return;
            }

            IsLoadingContributors = true;
            HasContributorLoadFailed = false;

            try
            {
                IReadOnlyList<GitHubRepositoryContributor> contributors =
                    await contributorService.GetContributorsAsync(cancellationToken);

                await appDispatcher.EnqueueAsync(() =>
                {
                    ContributorItems.Clear();
                    int rowIndex = 0;
                    foreach (GitHubRepositoryContributor contributor in contributors)
                    {
                        ContributorItems.Add(new ContributorItemViewModel(
                            CreateContributorDisplayName(contributor),
                            localizationService.FormatString("AboutDialog.ContributionCountFormat", contributor.Contributions),
                            contributor.Login.Length >= 2 ? contributor.Login[..2].ToUpperInvariant() : contributor.Login.ToUpperInvariant(),
                            localizationService.GetString("AboutDialog.OpenProfile"),
                            contributor.ProfileUri,
                            contributor.AvatarUri,
                            rowIndex % 2 == 1));
                        rowIndex++;
                    }

                    hasLoadedContributors = ContributorItems.Count > 0;
                    HasContributorLoadFailed = ContributorItems.Count == 0;
                    RaiseContributorStateChanged();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                HasContributorLoadFailed = true;
            }
            finally
            {
                IsLoadingContributors = false;
                RaiseContributorStateChanged();
            }
        }

        private void RaiseContributorStateChanged()
        {
            OnPropertyChanged(nameof(ContributorsListVisibility));
            OnPropertyChanged(nameof(ContributorsErrorVisibility));
        }

        private static string CreateContributorDisplayName(GitHubRepositoryContributor contributor)
        {
            return string.IsNullOrWhiteSpace(contributor.DisplayName)
                ? $"@{contributor.Login}"
                : $"{contributor.DisplayName} (@{contributor.Login})";
        }

        private ThirdPartyLicenseItemViewModel CreateThirdPartyLicenseItem(
            string name,
            string license,
            string licenseUrl,
            string homepageUrl,
            bool isAlternate)
        {
            return new ThirdPartyLicenseItemViewModel(
                name,
                license,
                new Uri(licenseUrl),
                localizationService.GetString("AboutDialog.HomepageLink"),
                new Uri(homepageUrl),
                isAlternate);
        }

        [RelayCommand]
        private async Task OpenUriAsync(Uri uri)
        {
            await externalProcessLauncher.OpenUriAsync(uri);
        }
    }

    public sealed record ThirdPartyLicenseItemViewModel(
        string Name,
        string License,
        Uri LicenseUri,
        string HomepageText,
        Uri HomepageUri,
        bool IsAlternate);

    public sealed record ContributorItemViewModel(
        string Name,
        string Role,
        string Initials,
        string LinkText,
        Uri ProfileUri,
        Uri AvatarUri,
        bool IsAlternate);
}
