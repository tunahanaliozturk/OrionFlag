<p align="center">
  <img src="docs/logo.png" alt="OrionFlag" width="150" />
</p>

# OrionFlag

[![CI/CD](https://github.com/tunahanaliozturk/OrionFlag/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/tunahanaliozturk/OrionFlag/actions/workflows/ci-cd.yml)
[![NuGet](https://img.shields.io/nuget/v/OrionFlag.svg)](https://www.nuget.org/packages/OrionFlag/)

**Feature flags that evaluate in-process with zero network per check.** A flag lookup is a lock-free, allocation-free read of an immutable snapshot — never a config-file redeploy and never a call to a SaaS console.

Teams reach for flags to ship dark, roll out gradually, and kill a bad feature without a redeploy, but the usual roads hurt: `appsettings.json` + `IOptionsMonitor` is free but a flag change is a config deploy with no per-request stability; a SaaS makes every check a network dependency you must cache, with your rollout rules and audit trail in someone else's console. OrionFlag keeps evaluation local and — in later waves — keeps your audit trail and redaction inside your own database.

## Features

- **In-process evaluation** — `IsEnabled` reads an immutable `FrozenDictionary` snapshot: lock-free, allocation-free, no network. The allocation claim is measured in the suite with `GC.GetAllocatedBytesForCurrentThread()`, including with a `MeterListener` attached.
- **`IOptionsMonitor` bridge** — flags are `OrionFlagOptions` bound from any config section, so existing `appsettings` boolean flags migrate with no code change and reloads flow through live.
- **Per-request snapshots** — capture `GetSnapshot()` at the start of a request and a flag can't flip mid-request even if config reloads underneath you.
- **Stable percentage rollout** — `IsEnabledFor(flag, subject)` gives a subject the same cohort across requests, snapshots, and processes. A disabled boolean flag always wins; the existing `IsEnabled` API is unchanged. Rollouts use SHA-256 over length-prefixed UTF-8 flag name, salt, and subject; no subject identifier is emitted as a metric tag.
- **OpenTelemetry by default** — a `Moongazing.OrionFlag` meter with `orion.flag.evaluations`, tagged by the configured flag name, result, and defined status. Undefined names share a `<undefined>` label, so dynamic misses cannot create one metric series per input; `orion.flag.defined` distinguishes a configured flag literally named `<undefined>` from a miss. Both live and pinned-snapshot decisions are recorded once.
- **AOT- and trim-clean**, verified by a native-binary smoke test in CI. Multi-targets `net8.0`, `net9.0`, `net10.0`.

## Install

```bash
dotnet add package OrionFlag
```

## Usage

Configure in code, from configuration, or both:

```csharp
using Moongazing.OrionFlag;
using Moongazing.OrionFlag.DependencyInjection;

services.AddOrionFlag(o => o.Flags["checkout.new-flow"] = true);
// or bind an appsettings section (reloads flow through IOptionsMonitor):
services.Configure<OrionFlagOptions>(Configuration.GetSection("OrionFlags"));
```

```jsonc
// appsettings.json
"OrionFlags": {
  "DefaultWhenMissing": false,
  "Flags": { "checkout.new-flow": true, "legacy-import": false },
  "Rollouts": { "checkout.new-flow": { "Percentage": 25, "Salt": "launch-1" } }
}
```

```csharp
public sealed class CheckoutEndpoint(IOrionFlags flags)
{
    public IResult Handle()
    {
        if (flags.IsEnabled("checkout.new-flow"))
        {
            // new path
        }
        return Results.Ok();
    }
}
```

Pin a snapshot for a whole request so every check is consistent:

```csharp
var snapshot = flags.GetSnapshot();
if (snapshot.IsEnabled("checkout.new-flow")) { /* ... */ }
// ... even if config reloads here, snapshot does not change ...
if (snapshot.IsEnabled("checkout.new-flow")) { /* same answer */ }
```

For a gradual rollout, pass a stable, non-empty subject key (for example your internal customer ID):

```csharp
var snapshot = flags.GetSnapshot();
if (snapshot.IsEnabledFor("checkout.new-flow", customer.Id)) { /* new path */ }
```

`Percentage` is a whole number from 0 to 100. The configured boolean flag is the kill switch: `false` disables everyone; `true` without a rollout enables everyone. `IsEnabled(flag)` remains the context-free boolean check and does **not** apply the rollout, so call `IsEnabledFor` at every subject-specific decision point. A rollout referencing an absent flag or an out-of-range percentage fails configuration. Change `Salt` only when you intentionally want to reshuffle cohorts. This is a deterministic rollout, not an authorization boundary or an experiment analytics service; do not use it for security decisions.

An undefined flag is not the same thing as a flag configured `false`, even though both read `false`:

```csharp
var snapshot = flags.GetSnapshot();
snapshot.IsEnabled("chekout.new-flow"); // false — but only because the name is a typo
snapshot.IsDefined("chekout.new-flow"); // false — the flag was never configured at all
```

`DefaultWhenMissing` decides what a miss serves. Leave it `false` so a mistyped or not-yet-deployed flag **fails closed** — the feature stays off. Setting it `true` makes every unknown name fail *open*, so for a kill switch a typo turns the feature on; `IsDefined` is the only way a caller can tell the two apart.

## Roadmap

Version 1.0 stabilizes the in-process core: boolean flags, reloadable snapshots, telemetry, and opt-in stable percentage rollout. Planned work: an EF Core store with audited writes via [OrionAudit](https://github.com/tunahanaliozturk/OrionAudit), sensitive values redacted through [OrionShade](https://github.com/tunahanaliozturk/OrionShade), scheduled flags on [OrionClock](https://github.com/tunahanaliozturk/OrionClock), richer targeting rules, a minimal-API filter, A/B variants, and typed dynamic config. See [CHANGELOG.md](CHANGELOG.md).

OrionFlag is not an experimentation/analytics platform, not a general config-management pipeline, not a secrets manager (it will *redact* sensitive values in later waves; store secrets in [OrionVault](https://github.com/tunahanaliozturk/OrionVault)/Key Vault), and has no client-side/edge SDKs — server-side .NET only.

## Versioning

Follows [Semantic Versioning](https://semver.org/). Multi-targets `net8.0`, `net9.0`, and `net10.0`. Binds to `Orion.Abstractions` 1.x.

## Documentation

- [CHANGELOG.md](CHANGELOG.md) — release notes.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) and the [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## More from the Orion family

Focused .NET libraries built to one quality bar. Each is usable on its own; several share the small [`Orion.Abstractions`](https://github.com/tunahanaliozturk/Orion.Abstractions) contracts spine, but there is no deep dependency web — pick only what you need:

- [Orion.Abstractions](https://github.com/tunahanaliozturk/Orion.Abstractions) — the shared contracts spine: telemetry, options, result, clock
- [OrionClock](https://github.com/tunahanaliozturk/OrionClock) — a `TimeProvider`-based clock with TTL / deadline vocabulary
- [OrionResult](https://github.com/tunahanaliozturk/OrionResult) — Result/Option types and a shared error vocabulary
- [OrionCache](https://github.com/tunahanaliozturk/OrionCache) — cache-aside with single-flight stampede protection
- [OrionResilience](https://github.com/tunahanaliozturk/OrionResilience) — retry, backoff, and timeout on OrionClock
- [OrionRate](https://github.com/tunahanaliozturk/OrionRate) — rate limiting on OrionClock
- [OrionPage](https://github.com/tunahanaliozturk/OrionPage) — keyset/cursor pagination for EF Core
- [OrionEnvelope](https://github.com/tunahanaliozturk/OrionEnvelope) — one HTTP contract: envelope + problem+json
- [OrionAudit](https://github.com/tunahanaliozturk/OrionAudit) — automatic EF Core change-audit trail
- [OrionShade](https://github.com/tunahanaliozturk/OrionShade) — sensitive-data redaction for logs and telemetry
- [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard) — validation, guard clauses, DDD primitives, domain events
- [OrionBeacon](https://github.com/tunahanaliozturk/OrionBeacon) — leader election with fencing tokens
- [OrionGrant](https://github.com/tunahanaliozturk/OrionGrant) — permission / authorization checks
- [OrionInbox](https://github.com/tunahanaliozturk/OrionInbox) — transactional inbox for exactly-once effects
- [OrionKey](https://github.com/tunahanaliozturk/OrionKey) — source-generated strongly-typed IDs
- [OrionLedger](https://github.com/tunahanaliozturk/OrionLedger) — API-key issuance, verification, and rotation
- [OrionLens](https://github.com/tunahanaliozturk/OrionLens) — ambient correlation-context propagation
- [OrionLock](https://github.com/tunahanaliozturk/OrionLock) — distributed locks with fencing tokens
- [OrionOnce](https://github.com/tunahanaliozturk/OrionOnce) — idempotency keys for exactly-once request handling
- [OrionPatch](https://github.com/tunahanaliozturk/OrionPatch) — transactional outbox for EF Core
- [OrionRelay](https://github.com/tunahanaliozturk/OrionRelay) — outbound webhook delivery (HMAC, retries, backoff)
- [OrionSaga](https://github.com/tunahanaliozturk/OrionSaga) — sagas / process managers for long-running workflows
- [OrionStream](https://github.com/tunahanaliozturk/OrionStream) — server-sent events / streaming hub
- [OrionVault](https://github.com/tunahanaliozturk/OrionVault) — field-level encryption for EF Core

See it all working together in [OrionShowcase](https://github.com/tunahanaliozturk/OrionShowcase), a production-shaped banking sample.

## License

[MIT](LICENSE).
