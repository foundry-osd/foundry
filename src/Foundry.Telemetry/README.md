# Telemetry contracts

## Custom Windows images

Custom images extend the existing event taxonomy. `osd:boot_media_finished` includes `boot_media_custom_images_enabled`, `boot_media_custom_images_count`, and `boot_media_default_os_source` (`catalog` or `custom`). The count comes from included images in the operation's captured configuration, is zero when disabled, and represents the intended selection when media creation fails or is cancelled.

`deploy:session_finished` includes `deploy_os_source` (`catalog` or `custom`). OS properties are populated from the selected source's metadata, including the full WIM version and default language. `TelemetryEventPropertyPolicy` validates custom metadata before sending it: known technical values are retained; missing or invalid values become `unknown`. Custom license channel and media update month remain `unknown`. The existing numeric applied index, outcome, duration, and failure fields are retained.

No image names, index names, paths, hashes, or image/profile identifiers are added to Product Analytics. Catalog metadata keeps its existing contract. Import lifecycle messages remain diagnostic logs governed by remote diagnostics consent. The telemetry and debug controls are unchanged; no separate import event is introduced.

## Post-installation configuration

`osd:boot_media_finished` includes a snapshot of the media operation's post-installation configuration:

| Property | Value |
| --- | --- |
| `customization_post_installation_enabled` | Boolean state of the Post-installation page. |
| `customization_post_installation_configured_action_count` | All configured actions, including disabled entries and entries on a disabled page. |
| `customization_post_installation_enabled_action_count` | Enabled actions when the page is enabled; otherwise zero. |
| `customization_post_installation_powershell_count` | Enabled PowerShell actions when the page is enabled; otherwise zero. |
| `customization_post_installation_command_count` | Enabled command actions when the page is enabled; otherwise zero. |
| `customization_post_installation_software_count` | Enabled software installation actions when the page is enabled; otherwise zero. |
| `customization_post_installation_restart_count` | Enabled restart actions when the page is enabled; otherwise zero. |

Each count is an integer capped at 1,000. The event describes the captured configuration, including when media creation fails or is cancelled, and does not report runtime execution. The property policy accepts only a Boolean enabled value and integer counts from zero through 1,000; it rejects invalid types, out-of-range values, and these properties on unrelated events.

Commands, arguments, action or software names and identifiers, hashes, paths, and script content are excluded. Existing telemetry consent and debug controls are unchanged. No PostInstall runtime events are introduced.

## Exception grouping

`PostHogExceptionTracker` sets `$exception_fingerprint` so PostHog Error Tracking groups issues by stable failure context:

| Record | Fingerprint |
| --- | --- |
| Carries `failure.code`, `failure.kind`, or `failure.operation` | `service.name`, exception type, logical operation, `process.operation`, `tool.name`, `failure.reason`, `failure.code`, `process.exit_code`, and `workflow.step`. |
| Has no stack trace and none of those attributes | The same values without `workflow.step`. |
| Has a stack trace and none of those attributes | None; PostHog groups by stack trace. |

A record that only carries `failure.reason` (for example a startup stage) is not a domain failure, so with a stack trace it keeps PostHog's stack-based grouping.

The logical operation is the first non-empty value of `failure.operation`, `operation.name`, or `process.operation`. Domain failures are typically thrown from one shared helper, so stack-based grouping would merge unrelated failure codes into one issue; suppressing that issue in PostHog would then drop every new failure thrown from the same site. Fingerprints use only sanitized, bounded, low-cardinality attributes and never include messages, summaries, session or operation identifiers, durations, paths, or process output.

## Exception delivery diagnostics

Error Tracking uses the PostHog SDK's in-memory queue. A successful `Capture` means the SDK accepted the event, not that PostHog received it. SDK 2.15.5 can discard an older event while accepting a new one when the queue is full.

Delivery problems produce a warning with `SourceContext=Foundry.Telemetry.ExceptionDelivery`, `FailureReason`, and an optional numeric `HttpStatusCode`:

| Reason | Meaning |
| --- | --- |
| `capture_rejected` | The SDK returned false from capture; this does not establish queue saturation. |
| `export_exception` | Exporting a queued exception threw before completing. |
| `sdk_queue_drop_oldest` | The SDK reported its full queue and oldest-item drop policy. This is not an exact loss counter. |
| `http_failure` | A background batch or explicit SDK flush failed with an HTTP/API exception. |
| `batch_exception` | The SDK background batch worker reported another exception. |
| `flush_exception` | Explicit flushing reported another exception. |
| `dispose_exception` | Exporter disposal threw during shutdown. |

The same fixed warning is written locally and queued directly to the independent Logs pipeline while diagnostics consent remains valid. It never contains SDK messages, request bodies, response bodies, URLs, or raw exceptions. The transport marker prevents normal logging from recapturing it; failures of Logs delivery stay local to prevent loops.

Shutdown drains and disposes Error Tracking before closing Logs so failures observed during SDK flush and final disposal can be retained. The caller's shutdown deadline bounds waiting; callbacks arriving after that deadline can only be written locally. Disabling diagnostics invalidates old delivery callbacks; re-enabling creates a new exporter. These diagnostics improve visibility without adding retry guarantees or durable Error Tracking storage.

The logger bridge intentionally recognizes SDK 2.15.5 events `AsyncBatchHandler/111`, `AsyncBatchHandler/500`, and `PostHogClient/24` for `FlushAsync`. Verify these against the SDK source and run the real SDK transport tests when upgrading PostHog. Tests intercept HTTP through an in-memory handler and never contact a live project.
