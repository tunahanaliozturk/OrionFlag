# OrionFlag

In-process feature flags for .NET: boolean flags and stable percentage rollouts read from your configuration, evaluated from an immutable snapshot with no network call per check.

![How IsEnabled and IsEnabledFor decide a flag](https://raw.githubusercontent.com/tunahanaliozturk/OrionFlag/master/docs/diagrams/evaluate-flag.png)

## Install

    dotnet add package OrionFlag

OrionFlag references only `Microsoft.Extensions.DependencyInjection.Abstractions`. The console quick start below builds its own container, so it also needs the container package (an ASP.NET Core or Generic Host app already has it):

    dotnet add package Microsoft.Extensions.DependencyInjection

## Quick start

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionFlag;
using Moongazing.OrionFlag.DependencyInjection;

var services = new ServiceCollection();
services.AddOrionFlag(o =>
{
    o.Flags["checkout.new-flow"] = true;
    o.Rollouts["checkout.new-flow"] = new PercentageRolloutOptions { Percentage = 25, Salt = "launch-1" };
});

using var provider = services.BuildServiceProvider();
var flags = provider.GetRequiredService<IOrionFlags>();

bool on = flags.IsEnabled("checkout.new-flow");                 // boolean check, ignores the rollout
bool forCustomer = flags.IsEnabledFor("checkout.new-flow", "customer-42"); // 25% cohort, stable per subject

var snapshot = flags.GetSnapshot(); // pin for a request: a reload cannot flip it
bool defined = snapshot.IsDefined("checkout.new-flow");
```

In an ASP.NET Core app, bind configuration instead (reloads flow through `IOptionsMonitor`):

```csharp
using Moongazing.OrionFlag;
using Moongazing.OrionFlag.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOrionFlag();
builder.Services.Configure<OrionFlagOptions>(builder.Configuration.GetSection("OrionFlags"));
```

Outside ASP.NET Core, `Configure<T>(IConfiguration)` comes from the `Microsoft.Extensions.Options.ConfigurationExtensions` package.

```json
{
  "OrionFlags": {
    "DefaultWhenMissing": false,
    "Flags": { "checkout.new-flow": true },
    "Rollouts": { "checkout.new-flow": { "Percentage": 25, "Salt": "launch-1" } }
  }
}
```

## Options

- `Flags` - flag name to on/off, case-insensitive.
- `Rollouts` - optional `PercentageRolloutOptions` per flag: `Percentage` (whole number 0 to 100, default 0) and `Salt` (default empty; change it to reshuffle cohorts).
- `DefaultWhenMissing` - what an undefined name serves. Default `false`, so a typo fails closed. `IsDefined` tells a miss apart from a flag configured `false`.

## Behaviour

- A configured `false` always wins. `IsEnabled` never applies a rollout; `IsEnabledFor` does, using SHA-256 over the flag name, salt and subject, so a subject keeps its cohort across requests, snapshots and processes.
- A rollout for an absent flag, a percentage outside 0 to 100 or a null salt throws `ArgumentException` when `IOrionFlags` is first resolved; on a reload the previous snapshot keeps serving.
- A reload builds a new snapshot and swaps it in atomically. A snapshot from `GetSnapshot()` never changes.
- `IsEnabled` is a lock-free, allocation-free read. A rollout is a deterministic cohort, not an authorization boundary.

## Telemetry and AOT

- Meter `Moongazing.OrionFlag`, counter `orion.flag.evaluations` tagged with the flag name, result and defined status. Unknown names share one `<undefined>` tag value; subjects are never tagged.
- AOT- and trim-compatible (`IsAotCompatible`), checked by a NativeAOT smoke test in CI. Targets net8.0, net9.0 and net10.0.

## Related packages

- `Orion.Abstractions` - the family contracts; `FlagDiagnostics` is built on its `OrionInstrumentation`.

## Links

- Documentation and full README: https://github.com/tunahanaliozturk/OrionFlag
- Changelog: https://github.com/tunahanaliozturk/OrionFlag/blob/master/CHANGELOG.md
- License: MIT
