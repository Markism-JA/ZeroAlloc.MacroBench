# ZeroAlloc.Benchmarking

Low-level macro-benchmarking and performance-regression harness for stateful, multi-threaded .NET daemons and pipelines (64-bit AnyCPU).

> Extracted from `ZeroAlloc.Platform` (`Platform.Benchmarking.Harness`) and currently in pre-release development.

## Planned Scope

- **Environment hygiene:** process priority elevation, CPU governor/power-plan checks, topology validation.
- **Orchestration:** dedicated pinned worker threads, phase barriers, warmup drivers.
- **Open-loop load pacing:** fixed-rate pacing without coordinated omission.
- **Telemetry, reporting & artifacts:** zero-alloc histograms, seqlock metrics readers, performance budget assertions, and machine-readable artifact export (JSON/CSV/Markdown).

## License

[MIT](./LICENSE) © 2026 Markism-JA
