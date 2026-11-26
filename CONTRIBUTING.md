# Contributing to ZeroAlloc.MacroBench

Thank you for your interest in contributing to ZeroAlloc.MacroBench. This is a focused, solo-maintained macro-benchmarking harness—keeping changes small, well-tested, and tightly scoped helps keep measurements trustworthy across platforms.

## Before You Start

- Open an issue first before starting work on new telemetry primitives, generator attributes, or public API changes so we can align on scope and design.
- Bug fixes, documentation improvements, and tests are always welcome via pull request.
- AI-assisted contributions are fine, provided you have read the relevant module, understand its safety invariants, and take full responsibility for every line in the PR.
- Maintenance happens on a best-effort schedule; PRs outside the library's core scope may be declined.

## Development Requirements

- .NET SDK matching `global.json` (targets `net10.0`, with the bundled Roslyn generator targeting `netstandard2.0`).
- Zero warnings: builds must succeed with `0 Warning(s)` and `0 Error(s)` in `Release` mode, including `CS1591` XML documentation on all public types and members.
- Cross-platform parity: changes must pass across Linux, Windows, and macOS (`x64` and `ARM64`).

### Local Verification

```bash
dotnet build ZeroAlloc.MacroBench.slnx --no-incremental -c Release
dotnet test ZeroAlloc.MacroBench.slnx -c Release --no-build
dotnet test ZeroAlloc.MacroBench.slnx -c Debug
```

## Expectations for Concurrency, `unsafe`, & Generator Code

1. Tests are mandatory: PRs touching histograms, pacers, Seqlocks, `PhaseBarrier`, or the Roslyn generator must include unit/concurrency tests.
2. Preserve measurement integrity: never introduce managed allocations on hot recording/pacing paths (`NativeHistogramHandle.Record`, `OpenLoopPacer.WaitNext`, `SeqlockMetricsReader`).
3. Keep external dependencies minimal: avoid adding graphics, plotting, or heavy logging framework dependencies to the core library.
