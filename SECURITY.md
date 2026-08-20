# Security policy

Foundry builds boot media, performs privileged deployment operations, and can process deployment credentials and configuration. Please report suspected vulnerabilities privately so maintainers can investigate before details are disclosed publicly.

## Supported versions

| Version | Supported |
| --- | --- |
| Latest published release | Yes |
| Older published releases | No |

The latest release is available from [GitHub Releases](https://github.com/foundry-osd/foundry/releases/latest). Security fixes are not routinely backported. Reports affecting current development, CI, release automation, or the software supply chain are welcome even when the issue is not present in the latest release.

## Report a vulnerability

Use [GitHub private vulnerability reporting](https://github.com/foundry-osd/foundry/security/advisories/new). Do not open a public issue or pull request for an undisclosed vulnerability.

Include:

- The affected Foundry version and architecture.
- The affected component or workflow.
- Reproduction steps and the expected security impact.
- A minimal proof of concept, if safe to provide.
- Any known mitigations or workarounds.
- Your disclosure and credit preferences.

Do not submit real tenant credentials, tokens, passwords, certificate private keys, Wi-Fi or 802.1X secrets, Autopilot profiles, hardware hashes, device identifiers, or unsanitized deployment logs. A maintainer will arrange a safer exchange if additional sensitive evidence is required.

Maintainers will acknowledge a complete report as soon as practical, validate its scope and impact, and provide updates when there is material progress. Remediation timing depends on severity and release risk; fixed dates are not guaranteed. Please coordinate public disclosure with the maintainers.

## Scope

Relevant reports include, but are not limited to:

- Credential or sensitive-data exposure.
- Bypass of deployment-media protection.
- Unsafe disk selection or destructive action without the documented confirmation boundary.
- Privilege-boundary violations or arbitrary code execution.
- Release, update, or dependency supply-chain compromise.
- Information disclosure caused by Foundry.

Support questions, expected behavior on unsupported releases, findings that only affect upstream Microsoft or OEM services, and automated scanner output without a reproducible Foundry-specific impact should use the appropriate support channel instead.

## Good-faith research

Research only systems, devices, accounts, and data that you own or are authorized to test. Avoid privacy violations, service disruption, data destruction, and access beyond what is necessary to demonstrate the issue. Good-faith reports that follow this policy will not be treated as malicious activity by the Foundry maintainers.
