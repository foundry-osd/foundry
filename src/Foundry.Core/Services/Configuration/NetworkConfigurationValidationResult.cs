namespace Foundry.Core.Services.Configuration;

public sealed record NetworkConfigurationValidationResult(
    NetworkConfigurationValidationCode Code,
    IReadOnlyList<string> FormatArguments)
{
    public static NetworkConfigurationValidationResult Success { get; } = new(NetworkConfigurationValidationCode.None, []);

    public bool IsValid => Code == NetworkConfigurationValidationCode.None;

    public static NetworkConfigurationValidationResult Failure(
        NetworkConfigurationValidationCode code,
        params string[] formatArguments)
    {
        return new NetworkConfigurationValidationResult(code, formatArguments);
    }
}
