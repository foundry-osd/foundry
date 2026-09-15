# Support

Foundry is maintained as an open-source project. Support is provided by the community on a best-effort basis; no response time or service-level agreement is guaranteed.

## Before asking for help

Start with the [documentation](https://docs.foundryosd.com) and [troubleshooting guides](https://docs.foundryosd.com/troubleshooting). Search [existing issues](https://github.com/foundry-osd/foundry/issues) before opening a new one.

## Secure connection failures in Deploy

Deploy validates HTTPS server certificates using Windows trust and proxy settings. If a catalog or download cannot establish a secure connection, check the device date and time and the trusted root/intermediate certificates available inside the boot media. A corporate HTTPS inspection proxy must also have an approved certificate chain trusted by that WinPE environment; trusting it only on the authoring workstation is insufficient. Correct the clock or boot-media trust configuration and restart Deploy.

TLS handshake failures stop without repeated retries. During startup, Deploy records clock and certificate guidance in the diagnostic logs and closes through the existing Bootstrap failure handoff. During deployment, the guidance appears on the error page in the selected language. The original exception remains available as the inner exception in the diagnostic logs. The existing HTTP compatibility path for Microsoft delivery hosts is unchanged.

Developers can run the local certificate-rejection regressions without changing certificate stores:

```powershell
dotnet run --project src/Foundry.Deploy.Tests/Foundry.Deploy.Tests.csproj -c Release -p:Platform=x64 -- -class Foundry.Deploy.Tests.DeploymentHttpClientTests
```

An optional live check validates the public catalogs and valid, expired, wrong-host and self-signed certificates through the actual Deploy clients. It requires network access to GitHub and `badssl.com`; TLS inspection may change the certificates seen by the client:

```powershell
dotnet run --project src/Foundry.Deploy.Tests/Foundry.Deploy.Tests.csproj -c Release -p:Platform=x64 -- -explicit only -class Foundry.Deploy.Tests.DeploymentHttpsSmokeTests
```

## Choose the right channel

- **Reproducible bug:** use the [bug report form](https://github.com/foundry-osd/foundry/issues/new?template=bug-report.yml).
- **Feature or workflow improvement:** use the [feature request form](https://github.com/foundry-osd/foundry/issues/new?template=feature-request.yml).
- **Usage, setup, build, or contribution question:** use the [question form](https://github.com/foundry-osd/foundry/issues/new?template=question.yml).
- **Security vulnerability:** follow [SECURITY.md](SECURITY.md). Do not open a public issue.
- **Conduct concern:** follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

Include the Foundry OSD release, affected stage, workstation OS and architecture, deployment-media architecture, target device, reproduction steps, and sanitized evidence when relevant. Remove credentials, tokens, passwords, certificate material, tenant data, hardware hashes, device identifiers, network details, and personal information from logs, screenshots, and attachments.

Only the latest published Foundry release is supported. Older releases may still be useful for diagnosis, but fixes are delivered against the current release line.
