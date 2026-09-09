# Remote diagnostics

Foundry sends remote diagnostic logs and Error Tracking events only while remote diagnostics
are enabled. Usage telemetry remains a separate setting. Local logs and the existing
sanitized/raw support ZIP exports remain available for deeper investigation.

## Troubleshooting in PostHog

Start with an Error Tracking event, then find logs with the same `session.id` and
`operation.id`. Error Tracking also carries `$session_id`. Logs and exceptions use the
same sanitized record contract. Service version, release, runtime, and architecture are
already included; in OTLP logs, service/version fields are resource attributes.

Deployment logs include start, step start/completion, and terminal outcome records.
Process failures from the Deploy and Core WinPE runners include tool, exit code, duration,
and a recognized DISM operation when available. The terminal exception can be less detailed
than the preceding process warning: inspect both using their correlation fields.

For DISM image inspection, source existence and drive readiness/type are probed after
failure. These are best-effort observations, not proof of the cause. A removable-drive
classification does not identify the physical USB connection. Unavailable probes report
`unknown`. Image format and index are included where known.

## Retained and excluded information

- Technical fields use an explicit allowlist, including retry attempt/delay, step state,
  image context, and process metadata. Arbitrary properties remain excluded.
- DISM, 7-Zip (`7z.exe`/`7za.exe`), BCDBoot, Bootsect, and DiskPart output is filtered to
  recognized English error paragraphs/categories, sanitized, and limited to 8,192 characters
  per stream. Machine headers, image metadata, tables, and archive member names are excluded.
  Unrecognized/localized output falls back to tool/exit metadata. The last portion is retained when
  truncated, preserving final error lines. Output is sanitized before truncation.
  `process.output_filtered` indicates producer filtering; `process.output_omitted` indicates
  that supplied output had no exportable diagnostic text or came from an unreviewed tool.
- Other tools, including shells, PowerShell, Netsh, and Reg, retain metadata but omit
  output (`process.output_omitted=true`), since they can print unrestricted configuration
  or script payloads. Add a reviewed producer-specific diagnostic instead of enabling all
  output for these tools.
- Raw arguments, working directories, environment variables, response bodies, and full
  log attachments are not added to remote process records.
- Paths and URLs are redacted; common credentials, identifying labels, emails, and GUIDs
  are sanitized. JSON credential values and common XML secret elements are also removed.
  Pattern matching is not a general-purpose detector of unlabeled personal data: new
  output producers require review and representative tests.
- Known I/O, access, and timeout exceptions retain sanitized messages. Native exceptions
  retain their error code and system-provided description; HTTP exceptions retain their
  error category and status. Other exception messages use the log summary/type unless
  they implement `IRemoteDiagnosticException` with a reviewed technical explanation.
- Stack frames remain available when supplied, with local source paths removed. An
  exception without a stack is grouped by technical failure context rather than just
  its numeric code. Correlation IDs do not become error-group fingerprints.

## Delivery limits

Delivery remains best effort, with bounded in-memory queues and existing shutdown flushing.
There is no durable offline outbox or guarantee of delivery after sudden shutdown.
Repeated records are limited per operation, step, tool, and failure context. Marked terminal
outcomes bypass this repetition limit, but remain subject to queue capacity and consent.

`diagnostics.dropped_record_count` reports cumulative locally observed filtering/queue losses
on subsequent accepted records. It does not count losses inside SDK or network queues and
does not mean PostHog received every other record. No production events are emitted by tests.

When extending diagnostics, test both useful retained explanations and credential/path
removal. Keep consent handling and raw local support exports independent of remote capture.
