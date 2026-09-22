namespace Moongazing.OrionFlag;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Evaluates feature flags in-process, with no network call per check. A miss returns the configured
/// default rather than throwing, so a flag lookup never breaks business code.
/// <para>
/// For a single decision, call <see cref="IsEnabled(string)"/> / <see cref="IsEnabledAsync"/>. To keep
/// every check within one request consistent even across a live config reload, capture a
/// <see cref="GetSnapshot"/> at the start of the request and query it.
/// </para>
/// </summary>
public interface IOrionFlags
{
    /// <summary>Whether <paramref name="flag"/> is on right now (reads the current snapshot).</summary>
    /// <param name="flag">The flag name (case-insensitive).</param>
    bool IsEnabled(string flag);

    /// <summary>Whether <paramref name="flag"/> is on, or <paramref name="defaultValue"/> if the flag is unknown.</summary>
    /// <param name="flag">The flag name (case-insensitive).</param>
    /// <param name="defaultValue">The value to return when the flag is not present.</param>
    bool IsEnabled(string flag, bool defaultValue);

    /// <summary>Async-shaped flag check; completes synchronously over the in-memory store (durable stores arrive later).</summary>
    /// <param name="flag">The flag name (case-insensitive).</param>
    /// <param name="cancellationToken">Cancellation token. An already-cancelled token yields a cancelled result rather than an answer.</param>
    ValueTask<bool> IsEnabledAsync(string flag, CancellationToken cancellationToken = default);

    /// <summary>Capture the current flags as an immutable snapshot, stable for the duration you hold it.</summary>
    /// <returns>A point-in-time snapshot.</returns>
    FlagSnapshot GetSnapshot();
}
