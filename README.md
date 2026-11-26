# ZeroAlloc.MacroBench

Low-level macro-benchmarking and performance-regression harness for stateful, multi-threaded .NET daemons and pipelines (64-bit AnyCPU).

> Extracted from `ZeroAlloc.Platform` and currently in active pre-release development.

## Scope

- Environment hygiene: process priority elevation, CPU governor/power-plan checks, host environment capture.
- Orchestration: dedicated pinned worker threads, phase barriers, multi-phase lifecycle (`Setup -> Warmup -> Measure -> Teardown`).
- Open-loop load pacing: fixed-rate pacing without coordinated omission (`scheduledNs`) and schedule-slip tracking.
- Telemetry & budget gating: cache-resident blittable HDR histogram, Seqlock live metrics, GC delta tracking, and hard regression budgets.
- Artifact export: Console summary tables, JSON, Markdown, and CSV exports.

## License

[MIT](./LICENSE) © 2026 Markism-JA
