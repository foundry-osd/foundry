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
        private bool hasLoadedContributors;

        public AboutUsSettingViewModel(
            Foundry.Services.Localization.IApplicationLocalizationService localizationService,
            IGitHubRepositoryContributorService contributorService,
            IAppDispatcher appDispatcher)
        {
            this.localizationService = localizationService;
            this.contributorService = contributorService;
            this.appDispatcher = appDispatcher;
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
                    foreach (GitHubRepositoryContributor contributor in contributors)
                    {
                        ContributorItems.Add(new ContributorItemViewModel(
                            CreateContributorDisplayName(contributor),
                            localizationService.FormatString("AboutDialog.ContributionCountFormat", contributor.Contributions),
                            contributor.Login.Length >= 2 ? contributor.Login[..2].ToUpperInvariant() : contributor.Login.ToUpperInvariant(),
                            localizationService.GetString("AboutDialog.OpenProfile"),
                            contributor.ProfileUri,
                            contributor.AvatarUri));
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
