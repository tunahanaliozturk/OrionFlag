namespace Moongazing.OrionFlag.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moongazing.Orion.Abstractions.Diagnostics;
using Moongazing.OrionFlag.Diagnostics;
using Moongazing.OrionFlag.DependencyInjection;

using Xunit;

public sealed class OrionFlagTests
{
    private static InMemoryOrionFlags Create(out TestOptionsMonitor<OrionFlagOptions> monitor, Action<OrionFlagOptions>? configure = null)
    {
        var options = new OrionFlagOptions();
        configure?.Invoke(options);
        monitor = new TestOptionsMonitor<OrionFlagOptions>(options);
        return new InMemoryOrionFlags(monitor, new FlagDiagnostics());
    }

    [Fact]
    public void Known_flags_return_their_configured_value()
    {
        using var flags = Create(out _, o => { o.Flags["a"] = true; o.Flags["b"] = false; });
        Assert.True(flags.IsEnabled("a"));
        Assert.False(flags.IsEnabled("b"));
    }

    [Fact]
    public void Unknown_flags_return_the_default()
    {
        using var flags = Create(out _, o => o.DefaultWhenMissing = false);
        Assert.False(flags.IsEnabled("missing"));
        Assert.True(flags.IsEnabled("missing", defaultValue: true));

        using var onByDefault = Create(out _, o => o.DefaultWhenMissing = true);
        Assert.True(onByDefault.IsEnabled("missing"));
    }

    [Fact]
    public void Flag_names_are_case_insensitive()
    {
        using var flags = Create(out _, o => o.Flags["Checkout.NewFlow"] = true);
        Assert.True(flags.IsEnabled("checkout.newflow"));
        Assert.True(flags.IsEnabled("CHECKOUT.NEWFLOW"));
    }

    [Fact]
    public async Task Async_check_matches_the_sync_check()
    {
        using var flags = Create(out _, o => o.Flags["a"] = true);
        Assert.True(await flags.IsEnabledAsync("a"));
        Assert.False(await flags.IsEnabledAsync("nope"));
    }

    [Fact]
    public void A_config_reload_rebuilds_the_snapshot()
    {
        using var flags = Create(out var monitor, o => o.Flags["a"] = false);
        Assert.False(flags.IsEnabled("a"));

        monitor.Set(new OrionFlagOptions { Flags = { ["a"] = true } });
        Assert.True(flags.IsEnabled("a")); // picked up the reload
    }

    [Fact]
    public void A_captured_snapshot_does_not_flip_when_config_reloads()
    {
        using var flags = Create(out var monitor, o => o.Flags["a"] = false);
        var pinned = flags.GetSnapshot();

        monitor.Set(new OrionFlagOptions { Flags = { ["a"] = true } });

        Assert.False(pinned.IsEnabled("a")); // the pinned snapshot is stable...
        Assert.True(flags.IsEnabled("a"));   // ...while the live evaluator sees the new value
    }

    [Fact]
    public void Evaluations_are_recorded_to_telemetry()
    {
        using var diagnostics = new FlagDiagnostics();
        long count = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (i, l) => { if (OrionInstrumentation.ListensTo(i, diagnostics)) { l.EnableMeasurementEvents(i); } },
        };
        listener.SetMeasurementEventCallback<long>((_, v, _, _) => Interlocked.Add(ref count, v));
        listener.Start();

        var monitor = new TestOptionsMonitor<OrionFlagOptions>(new OrionFlagOptions { Flags = { ["a"] = true } });
        using var flags = new InMemoryOrionFlags(monitor, diagnostics);
        flags.IsEnabled("a");
        flags.IsEnabled("b");

        Assert.Equal(2, Interlocked.Read(ref count));
    }

    [Fact]
    public void AddOrionFlag_wires_a_usable_evaluator()
    {
        var services = new ServiceCollection();
        services.AddOrionFlag(o => o.Flags["a"] = true);
        using var provider = services.BuildServiceProvider();

        var flags = provider.GetRequiredService<IOrionFlags>();
        Assert.True(flags.IsEnabled("a"));
        Assert.False(flags.IsEnabled("b"));
    }
}

// A controllable IOptionsMonitor so a test can trigger a reload deterministically.
internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class
{
    private readonly List<Action<T, string?>> listeners = new();

    public TestOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; private set; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        listeners.Add(listener);
        return new Subscription(() => listeners.Remove(listener));
    }

    public void Set(T value)
    {
        CurrentValue = value;
        foreach (var listener in listeners.ToArray())
        {
            listener(value, null);
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
