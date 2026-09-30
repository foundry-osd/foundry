# Support

Foundry is maintained as an open-source project. Support is provided by the community on a best-effort basis; no response time or service-level agreement is guaranteed.

## Before asking for help

Start with the [documentation](https://docs.foundryosd.com) and [troubleshooting guides](https://docs.foundryosd.com/troubleshooting). Search [existing issues](https://github.com/foundry-osd/foundry/issues) before opening a new one.

After updating Foundry OSD, recreate ISO or USB media to pick up Bootstrap fixes for insufficient space on the WinPE `X:` drive. Runtime updates do not replace Bootstrap embedded in existing boot media. Bootstrap releases Connect's verified executable and extracted files after Connect exits. USB media retains verified update archives on its persistent cache volume; ISO media avoids retaining duplicate update archives on `X:`.

## Choose the right channel

- **Reproducible bug:** use the [bug report form](https://github.com/foundry-osd/foundry/issues/new?template=bug-report.yml).
- **Feature or workflow improvement:** use the [feature request form](https://github.com/foundry-osd/foundry/issues/new?template=feature-request.yml).
- **Usage, setup, build, or contribution question:** use the [question form](https://github.com/foundry-osd/foundry/issues/new?template=question.yml).
- **Security vulnerability:** follow [SECURITY.md](SECURITY.md). Do not open a public issue.
- **Conduct concern:** follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

Include the Foundry OSD release, affected stage, workstation OS and architecture, deployment-media architecture, target device, reproduction steps, and sanitized evidence when relevant. Remove credentials, tokens, passwords, certificate material, tenant data, hardware hashes, device identifiers, network details, and personal information from logs, screenshots, and attachments.

Only the latest published Foundry release is supported. Older releases may still be useful for diagnosis, but fixes are delivered against the current release line.
