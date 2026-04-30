namespace Foundry.ViewModels
{
    public sealed partial class AboutUsSettingViewModel : ObservableObject
    {
        public string Version => FoundryApplicationInfo.Version;
        public Uri RepositoryUri { get; } = new(FoundryApplicationInfo.RepositoryUrl);
        public Uri LatestReleaseUri { get; } = new(FoundryApplicationInfo.LatestReleaseUrl);
    }
}
