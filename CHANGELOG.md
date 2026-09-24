<!-- markdownlint-disable MD024 -->

# Changelog

All notable changes to OrionFlag are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/) and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Updated Microsoft.Extensions.DependencyInjection.Abstractions and Microsoft.Extensions.Options
  to 10.0.12 while retaining net8.0/net9.0/net10.0 support. Updated the test SDK, xUnit and
  test runner to current stable releases. The NuGet direct/transitive audit found no known
  vulnerable packages before this maintenance update.

### Added

- **`FlagSnapshot.IsDefined(flag)`** — tells an undefined flag apart from one configured `false`.
  `IsEnabled` serves `DefaultWhenMissing` for both, so a mistyped kill-switch name reads exactly like
  "the feature is off" — and with `DefaultWhenMissing = true` it fails *open*. `IsDefined` is the only
  way a caller can see the difference; the README now says which way a miss fails.
- Tests that measure rather than assert the claims the README makes: the snapshot lookup, the
  evaluation hot path with a `MeterListener` attached, and the async check each allocate 0 bytes over
  10,000 reads (`GC.GetAllocatedBytesForCurrentThread`); four reader threads racing 20,000 snapshot
  swaps never observe a half-applied update; `orion.flag.evaluations` carries the flag and result tags
  the docs promise, which the previous telemetry test (a bare measurement count) could not have caught.

### Fixed

- **Evaluation metric tags stay bounded by configured flags.** Unknown names share a single
  `<undefined>` tag; case variants of a known name use its configured spelling. A `defined` tag
  distinguishes the reserved-looking label from a configured flag with that literal name. Previously,
  1,000 distinct misses produced 1,000 distinct tag values.
- **Pinned snapshot evaluations now emit telemetry.** `GetSnapshot().IsEnabled(...)` previously
  returned the right stable answer without recording a decision, so a service using the documented
  request-pinning path disappeared from evaluation dashboards. Live and pinned paths now record
  once each, with the pinned snapshot's original value even after a reload.
- **A reload of a *named* `OrionFlagOptions` instance no longer replaces the evaluator's snapshot.**
  `IOptionsMonitor.OnChange` fires for every named instance, so an unrelated named section reloading
  swapped its flags into the evaluator seeded from the unnamed one — a wrong answer with nothing
  raised. The subscription now rebuilds only for the unnamed instance.
- **`IsEnabledAsync` honours its `CancellationToken`.** An already-cancelled token was ignored and an
  answer returned anyway; it now yields a cancelled `ValueTask`. The hot path stays allocation-free.

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
