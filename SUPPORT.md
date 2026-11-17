# Support

Foundry is maintained as an open-source project. Support is provided by the community on a best-effort basis; no response time or service-level agreement is guaranteed.

## Before asking for help

Start with the [documentation](https://docs.foundryosd.com) and [troubleshooting guides](https://docs.foundryosd.com/troubleshooting). Search [existing issues](https://github.com/foundry-osd/foundry/issues) before opening a new one.

Boot media run the Foundry WinPE (which includes Connect and Deploy) and PostInstall versions of the Foundry OSD release that created them and do not download newer versions at startup. After updating Foundry OSD, recreate your ISO media, PXE files, or USB drives without cache, and use **Update USB** on USB drives with cache, to get newer components and fixes, including Foundry WinPE fixes. Foundry WinPE (with Connect and Deploy) and PostInstall are stored in the boot image and are never downloaded, repaired, or unpacked at startup. Boot media created with Foundry OSD v26.10.18.1 or earlier download Connect and Deploy from the latest release at startup; they now show a blocking "This boot media is obsolete" screen and must be recreated with the current Foundry OSD. Such media that cannot reach GitHub (offline, or GitHub API rate limit) still start their embedded Deploy and deploy normally.

## Choose the right channel

- **Reproducible bug:** use the [bug report form](https://github.com/foundry-osd/foundry/issues/new?template=bug-report.yml).
- **Feature or workflow improvement:** use the [feature request form](https://github.com/foundry-osd/foundry/issues/new?template=feature-request.yml).
- **Usage, setup, build, or contribution question:** use the [question form](https://github.com/foundry-osd/foundry/issues/new?template=question.yml).
- **Security vulnerability:** follow [SECURITY.md](SECURITY.md). Do not open a public issue.
- **Conduct concern:** follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

Include the Foundry OSD release, affected stage, workstation OS and architecture, deployment-media architecture, target device, reproduction steps, and sanitized evidence when relevant. Remove credentials, tokens, passwords, certificate material, tenant data, hardware hashes, device identifiers, network details, and personal information from logs, screenshots, and attachments.

Only the latest published Foundry release is supported. Older releases may still be useful for diagnosis, but fixes are delivered against the current release line.
