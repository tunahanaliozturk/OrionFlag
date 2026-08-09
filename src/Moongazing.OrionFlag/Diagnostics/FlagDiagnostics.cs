namespace Moongazing.OrionFlag.Diagnostics;

using System.Collections.Generic;
using System.Diagnostics.Metrics;

using Moongazing.Orion.Abstractions.Diagnostics;

/// <summary>
/// OpenTelemetry instrumentation for flag evaluation. Built on the Orion family's
/// <see cref="OrionInstrumentation"/> spine: a <see cref="Meter"/> named <c>Moongazing.OrionFlag</c>
/// (subscribe by that name) carrying the <c>orion.flag.evaluations</c> counter, tagged with the flag
/// name (low cardinality — bounded by the number of flags) and the result. Recording uses the
/// discrete-tag counter overload with string tag values, so it allocates nothing on the evaluation
/// hot path and is a cheap early-out when no listener is attached.
/// <para>A process-wide <see cref="Shared"/> instance makes telemetry emit by default.</para>
/// </summary>
public sealed class FlagDiagnostics : OrionInstrumentation
{
    /// <summary>The meter name OpenTelemetry consumers subscribe to.</summary>
    public const string MeterName = "Moongazing.OrionFlag";

    /// <summary>The tag key carrying the evaluated flag name.</summary>
    public const string FlagTagKey = "orion.flag.flag";

    /// <summary>The tag key carrying the evaluation result.</summary>
    public const string ResultTagKey = "orion.flag.result";

    private static readonly System.Lazy<FlagDiagnostics> SharedInstance =
        new(static () => new FlagDiagnostics());

    /// <summary>Create the meter and its instrument.</summary>
    public FlagDiagnostics()
        : base(OrionTelemetry.ScopeName("OrionFlag"), MeterVersion.Value)
    {
        Evaluations = Meter.CreateCounter<long>(
            OrionTelemetry.MetricName("flag", "evaluations"),
            unit: "{evaluation}",
            description: "Flag evaluations, tagged with the flag name and result (enabled/disabled).");
    }

    /// <summary>The process-wide default instance, so telemetry emits without explicit wiring.</summary>
    public static FlagDiagnostics Shared => SharedInstance.Value;

    /// <summary>Counts flag evaluations.</summary>
    public Counter<long> Evaluations { get; }

    /// <summary>Record one evaluation of <paramref name="flag"/> resolving to <paramref name="enabled"/>.</summary>
    /// <param name="flag">The evaluated flag name.</param>
    /// <param name="enabled">The result.</param>
    public void RecordEvaluation(string flag, bool enabled) =>
        Evaluations.Add(
            1,
            new KeyValuePair<string, object?>(FlagTagKey, flag),
            new KeyValuePair<string, object?>(ResultTagKey, enabled ? "enabled" : "disabled"));
}
