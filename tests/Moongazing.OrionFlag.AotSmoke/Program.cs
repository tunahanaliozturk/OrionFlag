// NativeAOT smoke test. Publishing this with PublishAot=true must produce zero trim/AOT warnings,
// and running it must exit 0 - that pair is OrionFlag's AOT exit criterion. It exercises flag
// evaluation, the snapshot, the async check, the IOptionsMonitor bridge, and telemetry.
using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using Moongazing.OrionFlag;
using Moongazing.OrionFlag.Diagnostics;

var options = new OrionFlagOptions { DefaultWhenMissing = false };
options.Flags["checkout.new-flow"] = true;
options.Flags["legacy"] = false;
options.Flags["rollout"] = true;
options.Rollouts["rollout"] = new PercentageRolloutOptions { Percentage = 50, Salt = "v1" };

using var diagnostics = new FlagDiagnostics();
Check(diagnostics.Meter.Name == FlagDiagnostics.MeterName, "meter name wrong");

using var flags = new InMemoryOrionFlags(new StaticMonitor(options), diagnostics);

Check(flags.IsEnabled("checkout.new-flow"), "known-true flag should be enabled");
Check(!flags.IsEnabled("legacy"), "known-false flag should be disabled");
Check(!flags.IsEnabled("unknown"), "unknown flag should use the default");
Check(flags.IsEnabled("unknown", defaultValue: true), "explicit default should win");
Check(await flags.IsEnabledAsync("checkout.new-flow"), "async check failed");

var snapshot = flags.GetSnapshot();
Check(snapshot.IsEnabled("checkout.new-flow") && snapshot.Count == 3, "snapshot wrong");
Check(snapshot.IsEnabledFor("rollout", "customer-1") == flags.IsEnabledFor("rollout", "customer-1"), "rollout decision was not stable");
Check(snapshot.IsDefined("legacy"), "a configured-false flag should still be defined");
Check(!snapshot.IsDefined("unknown"), "an unconfigured flag should not be defined");

Console.WriteLine("OrionFlag AOT smoke test passed.");
return 0;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        Console.Error.WriteLine($"AOT smoke test failed: {message}");
        Environment.Exit(1);
    }
}

// A fixed options monitor (no reload) — enough to construct the evaluator under AOT.
internal sealed class StaticMonitor : IOptionsMonitor<OrionFlagOptions>
{
    public StaticMonitor(OrionFlagOptions value) => CurrentValue = value;

    public OrionFlagOptions CurrentValue { get; }

    public OrionFlagOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<OrionFlagOptions, string?> listener) => null;
}
