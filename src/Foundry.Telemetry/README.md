# Telemetry contracts

## Custom Windows images

Custom images extend the existing event taxonomy. `osd:boot_media_finished` includes `boot_media_custom_images_enabled`, `boot_media_custom_images_count`, and `boot_media_default_os_source` (`catalog` or `custom`). The count comes from included images in the operation's captured configuration, is zero when disabled, and represents the intended selection when media creation fails or is cancelled.

`deploy:session_finished` includes `deploy_os_source` (`catalog` or `custom`). OS properties are populated from the selected source's metadata, including the full WIM version and default language. `TelemetryEventPropertyPolicy` validates custom metadata before sending it: known technical values are retained; missing or invalid values become `unknown`. Custom license channel and media update month remain `unknown`. The existing numeric applied index, outcome, duration, and failure fields are retained.

No image names, index names, paths, hashes, or image/profile identifiers are added to Product Analytics. Catalog metadata keeps its existing contract. Import lifecycle messages remain diagnostic logs governed by remote diagnostics consent. The telemetry and debug controls are unchanged; no separate import event is introduced.

## Custom driver sets

`osd:boot_media_finished` includes `boot_media_custom_drivers_count`: the number of included driver sets in the operation's captured configuration, zero when the Custom drivers page is disabled. Like the custom image count, it represents the intended selection when media creation fails or is cancelled. The property policy accepts only an integer from zero through 256 (the driver set limit of a profile) on this event and drops it elsewhere.

Set names, identifiers, content hashes, and paths are never sent.

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

## Bounded identifiers

- `osd:boot_media_finished` sends `adk_version` only when the detected ADK version parses as a version of at most 64 characters, and always sends `adk_version_relation` (`unknown`, `below_supported`, `supported` or `above_supported`).
- `osd:boot_media_finished` sends `deployment_time_zone_id` only when a default time zone is selected; the policy keeps it only when it is a Windows time zone ID listed by the system, so free text and IANA IDs are dropped.
- `osd:boot_media_finished` always sends `boot_media_failure_kind` on failure (`unknown` when no diagnostic classified it) and never on success. OOBE diagnostic level and location access, and machine naming separator and casing, are sent only when their page option is enabled.
- `deploy:session_finished` sends the OOBE flags `deploy_oobe_enabled`, `deploy_oobe_administrator_enabled` and `deploy_oobe_account_creation_skipped` as Booleans, and `deploy_oobe_additional_account_count` as an integer from 0 through 100. No account names are sent.
- `bootstrap:failed` sends `payload_version`, the file version of the Connect or Deploy payload resolved for the failed stage (absent before any payload is resolved), and `boot_media_target` (`iso` or `usb`).

## Canonical values

Since `telemetry_schema_version` 3, `TelemetryValueNormalizer` rewrites values after validation, so each meaning has one spelling. Property names do not change, and every mapping keeps an already canonical value, so persisted `bootstrap:failed` records are revalidated without being dropped.

| Property | Canonical values |
| --- | --- |
| `boot_media_failure_kind`, `deploy_session_failure_reason` | `cancelled` for a cancellation (was `cancellation` and `caller_cancelled`). A cancelled deployment also sends `deploy_session_failure_kind = cancelled`. |
| `boot_media_failure_reason` | `non_zero_exit` (was `nonzero_exit`). |
| `boot_media_failure_code`, `deploy_session_failure_code` | Lowercase. |
| `boot_media_failure_tool` | Lowercase without `.exe`; `7zip`, `adk` and `makewinpemedia` for their display names; `other` for anything outside `[a-z0-9._-]{1,32}`. |
| `boot_media_creation_failed_step_name` | One of `MediaCreationStepNames`, otherwise `unknown`. |
| `deploy_driver_pack_selection_kind`, `deploy_autopilot_hash_upload_state`, `boot_media_boot_image_source`, `last_stage` | snake_case, for example `oem_catalog`, `upload_timed_out`, `winre_wifi`, `deployment_preparation`. `deploy_driver_pack_selection_kind` is one of `none`, `microsoft_update_catalog`, `oem_catalog` or `custom_driver_set`. |
| `deploy_os_edition` | Windows edition ID for catalog and custom images (`professional` for `Pro`, `core` for `Home`), otherwise `unknown`. |

## Exception grouping

`PostHogExceptionTracker` sets `$exception_fingerprint` so PostHog Error Tracking groups issues by stable failure context:

| Record | Fingerprint |
| --- | --- |
| Carries `failure.code`, `failure.kind`, or `failure.operation` | `service.name`, exception type, logical operation, `process.operation`, `tool.name`, `failure.reason`, `failure.code`, `process.exit_code`, and `workflow.step`, followed by `failure.detail` when it is set. |
| Has no stack trace and none of those attributes | The same values without `workflow.step`. |
| Has a stack trace and none of those attributes | None; PostHog groups by stack trace. |

A record that only carries `failure.reason` (for example a startup stage) is not a domain failure, so with a stack trace it keeps PostHog's stack-based grouping.

`failure.detail` is an optional fixed identifier that narrows a failure within its reason, for example the `copype.cmd` step that failed (`wim_mount_failed`, `boot_file_copy_failed`). It is never free text or tool output. Records without it keep the fingerprint they had before the attribute existed.

The logical operation is the first non-empty value of `failure.operation`, `operation.name`, or `process.operation`. Domain failures are typically thrown from one shared helper, so stack-based grouping would merge unrelated failure codes into one issue; suppressing that issue in PostHog would then drop every new failure thrown from the same site. Fingerprints use only sanitized, bounded, low-cardinality attributes and never include messages, summaries, session or operation identifiers, durations, paths, or process output.

## Exception delivery diagnostics

Error Tracking uses the PostHog SDK's in-memory queue. A successful `Capture` means the SDK accepted the event, not that PostHog received it. SDK 2.15.7 can discard an older event while accepting a new one when the queue is full.

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

The logger bridge intentionally recognizes SDK 2.15.7 events `AsyncBatchHandler/111`, `AsyncBatchHandler/500`, and `PostHogClient/24` for `FlushAsync`. Verify these against the SDK source and run the real SDK transport tests when upgrading PostHog. Tests intercept HTTP through an in-memory handler and never contact a live project.
