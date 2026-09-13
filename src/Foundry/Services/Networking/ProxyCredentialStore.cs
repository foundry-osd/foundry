// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Utilities.Security;

namespace Foundry.Services.Networking;

public sealed class ProxyCredential
{
    public ProxyCredential(string username, string domain, string password)
    {
        Username = username;
        Domain = domain;
        Password = password;
    }

    public string Username { get; }

    public string Domain { get; }

    public string Password { get; }
}

public interface IProxyCredentialStore
{
    ProxyCredential? Read();

    void Save(ProxyCredential credential);

    void Delete();
}

internal sealed class ProxyCredentialStore(IWindowsCredentialStore credentialStore) : IProxyCredentialStore
{
    private const string TargetName = "FoundryOSD/Proxy";

    public ProxyCredential? Read()
    {
        using WindowsCredential? credential = credentialStore.Read(TargetName);
        if (credential is null)
        {
            return null;
        }

        if (credential.Secret.Length % sizeof(char) != 0)
        {
            throw new InvalidDataException("The stored proxy credential has an invalid password encoding.");
        }

        string password = new(MemoryMarshal.Cast<byte, char>(credential.Secret));
        (string domain, string user) = SplitUsername(credential.UserName ?? string.Empty);
        return new ProxyCredential(user, domain, password);
    }

    public void Save(ProxyCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        string username = string.IsNullOrWhiteSpace(credential.Domain)
            ? credential.Username
            : $"{credential.Domain}\\{credential.Username}";
        credentialStore.Write(TargetName, MemoryMarshal.AsBytes(credential.Password.AsSpan()), username);
    }

    public void Delete()
    {
        credentialStore.Delete(TargetName);
    }

    private static (string Domain, string Username) SplitUsername(string value)
    {
        int separator = value.IndexOf('\\');
        return separator > 0
            ? (value[..separator], value[(separator + 1)..])
            : (string.Empty, value);
    }
}
