# Autohand C# SDK Documentation

The C# SDK is a thin .NET wrapper around the Autohand CLI JSON-RPC mode. Start with the repository README, then use these documents as the public API grows:

- `README.md` - install, quick start, streaming, and development commands.
- `examples/` - runnable .NET console apps for high-level agent workflows.
- `src/Autohand.CodeAgentSdk/` - source-of-truth public types.

The design center is application ergonomics: `Agent` for normal product code, `AutohandSdk` for low-level orchestration, typed event records for common event shapes, and raw `JsonElement` access wherever the CLI moves faster than the SDK.

