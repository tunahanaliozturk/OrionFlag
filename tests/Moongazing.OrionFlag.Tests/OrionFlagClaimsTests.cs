namespace Moongazing.OrionFlag.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;

using Moongazing.Orion.Abstractions.Diagnostics;
using Moongazing.OrionFlag.Diagnostics;

using Xunit;

/// <summary>
/// Pins the claims the README makes in so many words — "a lock-free, allocation-free read of an
/// immutable snapshot", per-request stability, and "a miss returns the configured default rather
/// than throwing". Each is measured or raced rather than asserted by inspection.
/// </summary>
public sealed class OrionFlagClaimsTests
{
    private const int HotPathIterations = 10_000;

    private static InMemoryOrionFlags Create(out TestOptionsMonitor<OrionFlagOptions> monitor, Action<OrionFlagOptions>? configure = null)
    {
        var options = new OrionFlagOptions();
        configure?.Invoke(options);
        monitor = new TestOptionsMonitor<OrionFlagOptions>(options);
        return new InMemoryOrionFlags(monitor, new FlagDiagnostics());
    }

    private static OrionFlagOptions Pair(bool value) =>
        new() { Flags = { ["a"] = value, ["b"] = value } };

    [Fact]
    public void A_snapshot_lookup_allocates_nothing()
    {
        using var flags = Create(out _, o => o.Flags["a"] = true);
        var snapshot = flags.GetSnapshot();

        for (var i = 0; i < HotPathIterations; i++)
        {
            _ = snapshot.IsEnabled("a");
            _ = snapshot.IsEnabled("missing");
            _ = snapshot.IsEnabled("missing", defaultValue: true);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < HotPathIterations; i++)
        {
            _ = snapshot.IsEnabled("a");
            _ = snapshot.IsEnabled("missing");
            _ = snapshot.IsEnabled("missing", defaultValue: true);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"snapshot lookups allocated {allocated} bytes over {HotPathIterations * 3} reads");
    }

    [Fact]
    public void The_evaluation_hot_path_allocates_nothing_while_a_meter_listener_is_attached()
    {
        // Without a subscriber the counter early-outs, so measuring an unlistened evaluator would
        // prove nothing about the recording path the README's claim covers. Attach one.
        using var diagnostics = new FlagDiagnostics();
        using var listener = new MeterListener
        {
            InstrumentPublished = (i, l) => { if (OrionInstrumentation.ListensTo(i, diagnostics)) { l.EnableMeasurementEvents(i); } },
        };
        listener.SetMeasurementEventCallback<long>(static (_, _, _, _) => { });
        listener.Start();

        var monitor = new TestOptionsMonitor<OrionFlagOptions>(new OrionFlagOptions { Flags = { ["a"] = true } });
        using var flags = new InMemoryOrionFlags(monitor, diagnostics);

        for (var i = 0; i < HotPathIterations; i++)
        {
            _ = flags.IsEnabled("a");
            _ = flags.IsEnabled("missing");
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < HotPathIterations; i++)
        {
            _ = flags.IsEnabled("a");
            _ = flags.IsEnabled("missing");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"evaluation allocated {allocated} bytes over {HotPathIterations * 2} checks with a listener attached");
    }

    [Fact]
    public async Task The_async_check_completes_synchronously_and_allocates_nothing()
    {
        using var flags = Create(out _, o => o.Flags["a"] = true);

        for (var i = 0; i < HotPathIterations; i++)
        {
            _ = await flags.IsEnabledAsync("a");
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < HotPathIterations; i++)
        {
            _ = await flags.IsEnabledAsync("a");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"the async check allocated {allocated} bytes over {HotPathIterations} checks");
    }

    [Fact]
    public async Task Concurrent_reads_never_observe_a_half_applied_update()
    {
        var monitor = new TestOptionsMonitor<OrionFlagOptions>(Pair(false));
        using var flags = new InMemoryOrionFlags(monitor, new FlagDiagnostics());

        var stop = false;
        var torn = 0;
        var reads = 0L;

        var readers = new Task[4];
        for (var r = 0; r < readers.Length; r++)
        {
            readers[r] = Task.Run(() =>
            {
                while (!Volatile.Read(ref stop))
                {
                    // Both flags always move together, so a snapshot that disagrees with itself is
                    // an update applied field-by-field rather than by one reference swap.
                    var snapshot = flags.GetSnapshot();
                    if (snapshot.IsEnabled("a") != snapshot.IsEnabled("b"))
                    {
                        Interlocked.Increment(ref torn);
                    }

                    Interlocked.Increment(ref reads);
                }
            });
        }

        for (var i = 0; i < 20_000; i++)
        {
            monitor.Set(Pair(i % 2 == 0));
        }

        Volatile.Write(ref stop, true);
        await Task.WhenAll(readers);

        Assert.True(Interlocked.Read(ref reads) > 1_000, $"only {Interlocked.Read(ref reads)} reads raced the writer; the race was never exercised");
        Assert.Equal(0, Volatile.Read(ref torn));
    }

    [Fact]
    public void A_pinned_snapshot_survives_many_reloads()
    {
        using var flags = Create(out var monitor, o => o.Flags["a"] = false);
        var pinned = flags.GetSnapshot();

        for (var i = 0; i < 100; i++)
        {
            monitor.Set(new OrionFlagOptions { Flags = { ["a"] = true } });
        }

        Assert.False(pinned.IsEnabled("a"));
        Assert.True(flags.IsEnabled("a"));
    }

    [Fact]
    public void An_empty_configuration_serves_the_default_from_an_empty_snapshot()
    {
        using var flags = Create(out _);
        var snapshot = flags.GetSnapshot();

        Assert.Equal(0, snapshot.Count);
        Assert.False(snapshot.IsEnabled("anything"));
        Assert.True(snapshot.IsEnabled("anything", defaultValue: true));
    }

    [Fact]
    public void A_null_or_empty_flag_name_throws_rather_than_serving_the_default()
    {
        using var flags = Create(out _, o => o.Flags["a"] = true);

        Assert.Throws<ArgumentNullException>(() => flags.IsEnabled(null!));
        Assert.Throws<ArgumentException>(() => flags.IsEnabled(string.Empty));
        Assert.Throws<ArgumentNullException>(() => flags.GetSnapshot().IsEnabled(null!));
    }

    [Fact]
    public void Evaluations_are_tagged_with_the_flag_name_and_the_result()
    {
        using var diagnostics = new FlagDiagnostics();
        var seen = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (i, l) => { if (OrionInstrumentation.ListensTo(i, diagnostics)) { l.EnableMeasurementEvents(i); } },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? flag = null;
            string? result = null;
            foreach (var tag in tags)
            {
                if (tag.Key == FlagDiagnostics.FlagTagKey)
                {
                    flag = tag.Value as string;
                }
                else if (tag.Key == FlagDiagnostics.ResultTagKey)
                {
                    result = tag.Value as string;
                }
            }

            lock (seen)
            {
                seen.Add($"{flag}={result}");
            }
        });
        listener.Start();

        var monitor = new TestOptionsMonitor<OrionFlagOptions>(new OrionFlagOptions { Flags = { ["on"] = true, ["off"] = false } });
        using var flags = new InMemoryOrionFlags(monitor, diagnostics);
        flags.IsEnabled("on");
        flags.IsEnabled("off");
        flags.IsEnabled("unknown");

        string[] expected = ["on=enabled", "off=disabled", "unknown=disabled"];
        Assert.Equal(expected, seen);
    }

    [Fact]
    public void A_reload_of_a_named_options_instance_does_not_replace_the_default_snapshot()
    {
        using var flags = Create(out var monitor, o => o.Flags["a"] = false);
        Assert.False(flags.IsEnabled("a"));

        // Another component registered OrionFlagOptions under a name (a per-tenant section, say).
        // Its reload must not reach the evaluator bound to the unnamed instance.
        monitor.SetNamed("tenant-a", new OrionFlagOptions { Flags = { ["a"] = true } });

        Assert.False(flags.IsEnabled("a"));
    }

    [Fact]
    public async Task A_cancelled_token_cancels_the_async_check()
    {
        using var flags = Create(out _, o => o.Flags["a"] = true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await flags.IsEnabledAsync("a", cts.Token));
    }

    [Fact]
    public void An_undefined_flag_is_distinguishable_from_a_configured_false()
    {
        using var flags = Create(out _, o => o.Flags["configured-off"] = false);
        var snapshot = flags.GetSnapshot();

        // Both read false, which is exactly why the caller needs a way to tell them apart.
        Assert.False(snapshot.IsEnabled("configured-off"));
        Assert.False(snapshot.IsEnabled("typo-in-the-flag-name"));

        Assert.True(snapshot.IsDefined("configured-off"));
        Assert.False(snapshot.IsDefined("typo-in-the-flag-name"));
        Assert.Throws<ArgumentException>(() => snapshot.IsDefined(string.Empty));
    }

    [Fact]
    public void An_undefined_flag_serves_the_configured_default_even_when_that_default_is_on()
    {
        // DefaultWhenMissing = true makes an unknown flag fail *open*. Pinned so the hazard the
        // README now warns about cannot change silently.
        using var flags = Create(out _, o => o.DefaultWhenMissing = true);

        Assert.True(flags.IsEnabled("never-configured"));
        Assert.False(flags.GetSnapshot().IsDefined("never-configured"));
    }
}
