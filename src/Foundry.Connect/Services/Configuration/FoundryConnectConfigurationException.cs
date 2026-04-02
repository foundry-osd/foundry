namespace Foundry.Connect.Services.Configuration;

public sealed class FoundryConnectConfigurationException : Exception
{
    public FoundryConnectConfigurationException(string message)
        : base(message)
    {
    }

    public FoundryConnectConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
