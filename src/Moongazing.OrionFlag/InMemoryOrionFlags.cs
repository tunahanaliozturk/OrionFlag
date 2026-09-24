namespace Moongazing.OrionFlag;

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using Moongazing.OrionFlag.Diagnostics;

/// <summary>
/// The in-memory flag evaluator. Holds an immutable <see cref="FlagSnapshot"/> that is rebuilt when
/// the bound <see cref="OrionFlagOptions"/> reloads (the <c>IOptionsMonitor</c> bridge), and swapped
/// in atomically — so an evaluation always reads a consistent snapshot, and a config reload never
/// tears a check. Reads are lock-free and allocation-free.
/// </summary>
public sealed class InMemoryOrionFlags : IOrionFlags, IDisposable
{
    private readonly FlagDiagnostics diagnostics;
    private readonly IDisposable? changeSubscription;
    private volatile FlagSnapshot snapshot;

    /// <summary>Create the evaluator over <paramref name="options"/>, rebuilding on reload.</summary>
    /// <param name="options">The flag options monitor; the current value seeds the snapshot and reloads rebuild it.</param>
    /// <param name="diagnostics">The instrumentation evaluations are recorded to.</param>
    public InMemoryOrionFlags(IOptionsMonitor<OrionFlagOptions> options, FlagDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.diagnostics = diagnostics;
        snapshot = Build(options.CurrentValue);
        changeSubscription = options.OnChange((o, name) =>
        {
            // OnChange fires for *every* named OrionFlagOptions instance, not only the one this
            // evaluator was seeded from (IOptionsMonitor.CurrentValue is the unnamed instance).
            // Rebuilding on someone else's named reload would silently serve their flags here.
            if (string.IsNullOrEmpty(name))
            {
                snapshot = Build(o);
            }
        });
    }

    /// <inheritdoc />
    public bool IsEnabled(string flag) => snapshot.IsEnabled(flag);

    /// <inheritdoc />
    public bool IsEnabled(string flag, bool defaultValue) => snapshot.IsEnabled(flag, defaultValue);

    /// <inheritdoc />
    public bool IsEnabledFor(string flag, string subject) => snapshot.IsEnabledFor(flag, subject);

    /// <inheritdoc />
    public ValueTask<bool> IsEnabledAsync(string flag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(flag);
        return cancellationToken.IsCancellationRequested
            ? ValueTask.FromCanceled<bool>(cancellationToken)
            : new ValueTask<bool>(IsEnabled(flag));
    }

    /// <inheritdoc />
    public FlagSnapshot GetSnapshot() => snapshot;

    /// <inheritdoc />
    public void Dispose() => changeSubscription?.Dispose();

    private FlagSnapshot Build(OrionFlagOptions options) =>
        FlagSnapshot.From(options.Flags, options.Rollouts, options.DefaultWhenMissing, diagnostics);
}
