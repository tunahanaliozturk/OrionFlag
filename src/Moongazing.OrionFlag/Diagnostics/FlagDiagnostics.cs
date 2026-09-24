namespace Moongazing.OrionFlag.Diagnostics;

using System.Diagnostics;
using System.Diagnostics.Metrics;

using Moongazing.Orion.Abstractions.Diagnostics;

/// <summary>
/// OpenTelemetry instrumentation for flag evaluation. Built on the Orion family's
/// <see cref="OrionInstrumentation"/> spine: a <see cref="Meter"/> named <c>Moongazing.OrionFlag</c>
/// (subscribe by that name) carrying the <c>orion.flag.evaluations</c> counter. Tags contain the
/// configured canonical name (bounded by the number of flags) or one undefined-name value, the
/// result, and defined status. A stack-backed <see cref="TagList"/> with string tag values keeps
/// the evaluation hot path allocation-free and cheap when no listener is attached.
/// <para>A process-wide <see cref="Shared"/> instance makes telemetry emit by default.</para>
/// </summary>
public sealed class FlagDiagnostics : OrionInstrumentation
{
    /// <summary>The meter name OpenTelemetry consumers subscribe to.</summary>
    public const string MeterName = "Moongazing.OrionFlag";

    /// <summary>The tag key carrying the evaluated flag name.</summary>
    public const string FlagTagKey = "orion.flag.flag";

    /// <summary>The tag value used for any flag that is not configured.</summary>
    public const string UndefinedFlagTagValue = "<undefined>";

    /// <summary>The tag key carrying the evaluation result.</summary>
    public const string ResultTagKey = "orion.flag.result";

    /// <summary>The tag key distinguishing configured flags from undefined names.</summary>
    public const string DefinedTagKey = "orion.flag.defined";

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
    public void RecordEvaluation(string flag, bool enabled) => RecordEvaluation(flag, enabled, defined: true);

    internal void RecordEvaluation(string flag, bool enabled, bool defined)
    {
        var tags = new TagList
        {
            { FlagTagKey, flag },
            { ResultTagKey, enabled ? "enabled" : "disabled" },
            { DefinedTagKey, defined ? "true" : "false" },
        };
        Evaluations.Add(1, in tags);
    }
}
