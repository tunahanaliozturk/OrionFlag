namespace Moongazing.OrionFlag.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

using Moongazing.Orion.Abstractions.Diagnostics;
using Moongazing.OrionFlag.Diagnostics;

using Xunit;

public sealed class PercentageRolloutTests
{
    private static InMemoryOrionFlags Create(OrionFlagOptions options, out TestOptionsMonitor<OrionFlagOptions> monitor)
    {
        monitor = new TestOptionsMonitor<OrionFlagOptions>(options);
        return new InMemoryOrionFlags(monitor, new FlagDiagnostics());
    }

    [Fact]
    public void Boundaries_and_the_boolean_kill_switch_are_respected()
    {
        var options = new OrionFlagOptions();
        options.Flags["off"] = false;
        options.Flags["none"] = true;
        options.Flags["all"] = true;
        options.Flags["ordinary"] = true;
        options.Rollouts["off"] = new PercentageRolloutOptions { Percentage = 100 };
        options.Rollouts["none"] = new PercentageRolloutOptions { Percentage = 0 };
        options.Rollouts["all"] = new PercentageRolloutOptions { Percentage = 100 };
        using var flags = Create(options, out _);

        Assert.False(flags.IsEnabledFor("off", "customer-1"));
        Assert.False(flags.IsEnabledFor("none", "customer-1"));
        Assert.True(flags.IsEnabledFor("all", "customer-1"));
        Assert.True(flags.IsEnabledFor("ordinary", "customer-1"));
        Assert.False(flags.IsEnabledFor("unknown", "customer-1"));
        Assert.True(flags.IsEnabled("none")); // Context-free boolean API has not changed.
    }

    [Fact]
    public void The_same_subject_stays_in_one_cohort_across_snapshots_and_case_variants()
    {
        var options = new OrionFlagOptions();
        options.Flags["NewFlow"] = true;
        options.Rollouts["newflow"] = new PercentageRolloutOptions { Percentage = 37, Salt = "v1" };
        using var flags = Create(options, out var monitor);
        var pinned = flags.GetSnapshot();
        var before = new bool[200];
        for (var i = 0; i < before.Length; i++)
        {
            before[i] = flags.IsEnabledFor("newflow", $"user-{i}");
            Assert.Equal(before[i], flags.IsEnabledFor("NEWFLOW", $"user-{i}"));
        }

        Assert.Contains(true, before);
        Assert.Contains(false, before);
        var replacement = new OrionFlagOptions();
        replacement.Flags["NewFlow"] = true;
        replacement.Rollouts["NewFlow"] = new PercentageRolloutOptions { Percentage = 37, Salt = "v1" };
        monitor.Set(replacement);
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i], flags.IsEnabledFor("NewFlow", $"user-{i}"));
            Assert.Equal(before[i], pinned.IsEnabledFor("NewFlow", $"user-{i}"));
        }
    }

    [Fact]
    public void A_pinned_snapshot_keeps_its_rollout_after_a_reload_or_options_mutation()
    {
        var options = new OrionFlagOptions();
        options.Flags["a"] = true;
        options.Rollouts["a"] = new PercentageRolloutOptions { Percentage = 0 };
        using var flags = Create(options, out var monitor);
        var pinned = flags.GetSnapshot();
        options.Rollouts["a"].Percentage = 100;
        Assert.False(pinned.IsEnabledFor("a", "customer"));

        var replacement = new OrionFlagOptions();
        replacement.Flags["a"] = true;
        replacement.Rollouts["a"] = new PercentageRolloutOptions { Percentage = 100 };
        monitor.Set(replacement);
        Assert.False(pinned.IsEnabledFor("a", "customer"));
        Assert.True(flags.IsEnabledFor("a", "customer"));
    }

    [Fact]
    public void Invalid_rollouts_fail_configuration_instead_of_silently_serving_wrong_cohorts()
    {
        var missing = new OrionFlagOptions();
        missing.Rollouts["typo"] = new PercentageRolloutOptions { Percentage = 50 };
        Assert.Throws<ArgumentException>(() => Create(missing, out _));

        var invalid = new OrionFlagOptions();
        invalid.Flags["a"] = true;
        invalid.Rollouts["a"] = new PercentageRolloutOptions { Percentage = 101 };
        Assert.Throws<ArgumentException>(() => Create(invalid, out _));
        invalid.Rollouts["a"].Percentage = -1;
        Assert.Throws<ArgumentException>(() => Create(invalid, out _));
        invalid.Rollouts["a"].Percentage = 50;
        invalid.Rollouts["a"].Salt = null!;
        Assert.Throws<ArgumentException>(() => Create(invalid, out _));
    }

    [Fact]
    public void Subject_ids_are_never_emitted_as_metric_tags()
    {
        using var diagnostics = new FlagDiagnostics();
        var tagsSeen = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (OrionInstrumentation.ListensTo(instrument, diagnostics))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                tagsSeen.Add($"{tag.Key}={tag.Value}");
            }
        });
        listener.Start();

        var options = new OrionFlagOptions();
        options.Flags["a"] = true;
        options.Rollouts["a"] = new PercentageRolloutOptions { Percentage = 50 };
        using var flags = new InMemoryOrionFlags(new TestOptionsMonitor<OrionFlagOptions>(options), diagnostics);
        _ = flags.IsEnabledFor("a", "private-customer-id");

        Assert.NotEmpty(tagsSeen);
        Assert.DoesNotContain(tagsSeen, tag => tag.Contains("private-customer-id", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_subjects_throw_even_when_the_flag_is_missing()
    {
        using var flags = Create(new OrionFlagOptions(), out _);
        Assert.Throws<ArgumentNullException>(() => flags.IsEnabledFor("a", null!));
        Assert.Throws<ArgumentException>(() => flags.IsEnabledFor("a", ""));
        Assert.Throws<ArgumentNullException>(() => flags.IsEnabledFor(null!, "subject"));
    }
}
