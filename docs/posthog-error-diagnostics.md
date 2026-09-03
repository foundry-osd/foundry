# Remote error diagnostics

Foundry can send sanitized operational logs and exception details to PostHog when **Enable remote diagnostics** is enabled in Settings. This preference is separate from anonymous product telemetry and is enabled by default. Generated Foundry Connect and Foundry Deploy configurations carry the same choice.

## Data boundary

Remote diagnostics include warning, error, and fatal events, plus information events explicitly marked as terminal diagnostics. Approved fields include application and release context, a random session or operation identifier, workflow stage, duration, retry count when available, and stable failure categories or process/HTTP codes.

Before an event enters the delivery queue, Foundry applies an explicit property allowlist and removes or replaces paths, URLs, credentials, tokens, network identifiers, machine names, user names, and similar direct identifiers. Full commands, process output, local file locations, and support-bundle details remain in local logs.

PostHog receives the connection source IP as transport metadata during direct HTTPS delivery. Foundry does not add it to diagnostic attributes, and Error Tracking events disable GeoIP enrichment.

The setting is applied live. Disabling it stops acceptance of new remote records immediately; records already being transmitted may finish. Delivery uses a bounded in-memory queue with rate limiting and duplicate exception suppression. It does not create a persistent outbox, so diagnostics can be dropped when the queue is full, the process exits, the network is unavailable, or PostHog rejects a request. Diagnostic export failures never replace the original operation result.

## PostHog operations

Logs are delivered through PostHog's OTLP HTTP endpoint. Exceptions are also represented as PostHog `$exception` events so Error Tracking can group and triage them. Use the shared `operation.id` to correlate a terminal workflow log, product completion event, and exception issue.

Recommended operational views:

- Save log searches for `operation.outcome = failed`, grouped by `workflow.name`, `workflow.step`, `failure.kind`, and `failure.reason`.
- Alert on new or reopened Error Tracking issues and on material increases in exception volume.
- Add spike alerts for boot-media and deployment terminal failures, separated by release and failure reason.
- Review rate-limit and queue-drop signals alongside ingestion volume before treating event counts as exact failure counts.
- Add drop or suppression rules only after confirming that a fingerprint is expected and non-actionable; preserve new failure categories.

Never store PostHog project credentials in documentation or generated deployment media. Configure credentials through the existing build and runtime configuration path.
