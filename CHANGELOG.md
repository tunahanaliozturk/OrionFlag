<!-- markdownlint-disable MD024 -->

# Changelog

All notable changes to OrionFlag are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/) and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-07-29

The first release — the Orion family's Wave 1 feature-flag evaluation core: in-process, from
configuration, with per-request snapshots.

### Added

- **`IOrionFlags`** / **`InMemoryOrionFlags`** — in-process flag evaluation:
  - `IsEnabled(flag)` / `IsEnabled(flag, default)` / `IsEnabledAsync` — lock-free, allocation-free
    reads of an immutable snapshot; an unknown flag returns the configured default rather than throwing.
  - `GetSnapshot()` — an immutable `FlagSnapshot` to pin for a request, so a flag cannot flip
    mid-request even if configuration reloads.
- **`OrionFlagOptions`** — a case-insensitive flag map plus `DefaultWhenMissing`, bound from any config
  section (existing `appsettings` boolean flags migrate with no code change) or configured in code.
- **`IOptionsMonitor` bridge** — the evaluator rebuilds its `FrozenDictionary` snapshot and swaps it in
  atomically when the bound options reload.
- **OpenTelemetry by default** — `FlagDiagnostics` on the family's `OrionInstrumentation` spine: a
  `Moongazing.OrionFlag` meter with `orion.flag.evaluations`, tagged by flag and result. Recording is
  allocation-free on the evaluation hot path.
- **`AddOrionFlag`** — DI wiring (options, diagnostics, evaluator).
- Binds to `Orion.Abstractions` 1.2.0. Multi-targets `net8.0`/`net9.0`/`net10.0`; `IsAotCompatible`; a
  NativeAOT publish smoke test in CI.

### Scope

Wave 1 is the in-process evaluation core. Deliberately deferred: an EF Core store with audited writes
(OrionAudit) and redacted sensitive values (OrionShade) plus scheduled flags on OrionClock (W2); a
targeting-rule engine with percentage rollout and stable bucketing, a minimal-API filter, and A/B
variants (W3); and typed dynamic config with a management API (W4, GA).

### Verified

- Exit criteria met: the AOT smoke publishes trim/AOT-clean under `-warnaserror` and exits 0; the
  evaluation hot path is a lock-free `FrozenDictionary` read with no allocation. 8 tests green across
  `net8.0`/`net9.0`/`net10.0`, including config-reload rebuilds and per-request snapshot stability.
