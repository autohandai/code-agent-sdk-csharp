# Changelog

## Unreleased

### Added

- Typed community skill registry and installation APIs.
- Typed MCP server, tool, and server-configuration discovery APIs.
- Tri-state goal budget updates with explicit unchanged, set, and clear values.
- A reproducible three-metric startup performance gate and GitHub Actions CI.

### Fixed

- Send the canonical `alternative` permission decision for suggested commands.
- Serialize prompt streams and isolate each stream and observer with a bounded event subscription.
- Abort the CLI prompt when its stream consumer stops early so late events cannot reach the next stream.
- Decouple long-lived stdout/stderr readers from the caller's startup token.
- Fail pending requests, invalidate the transport, and terminate the child on malformed stdout or stdout EOF.
- Keep process-reader generations isolated across restart after a child exits.
- Remove pending requests when stdin writes fail.
- Serialize concurrent start and stop calls so only one child generation can be live.
- Roll back a partially started subprocess when startup configuration fails.
- Omit null legacy goal fields instead of accidentally clearing stored budgets.
