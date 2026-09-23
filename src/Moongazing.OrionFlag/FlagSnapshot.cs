namespace Moongazing.OrionFlag;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// An immutable, point-in-time view of every flag. Capturing a snapshot at the start of a request and
/// querying it throughout guarantees a flag cannot flip mid-request even if the underlying config
/// reloads. Lookups are lock-free and allocation-free (a <see cref="FrozenDictionary{TKey, TValue}"/>
/// read), so the hot path stays cheap.
/// </summary>
public sealed class FlagSnapshot
{
    private readonly FrozenDictionary<string, bool> flags;
    private readonly bool defaultWhenMissing;

    internal FlagSnapshot(FrozenDictionary<string, bool> flags, bool defaultWhenMissing)
    {
        this.flags = flags;
        this.defaultWhenMissing = defaultWhenMissing;
    }

    /// <summary>Whether <paramref name="flag"/> is on in this snapshot, or the snapshot default if it is unknown.</summary>
    /// <param name="flag">The flag name (case-insensitive).</param>
    public bool IsEnabled(string flag)
    {
        System.ArgumentException.ThrowIfNullOrEmpty(flag);
        return flags.TryGetValue(flag, out var value) ? value : defaultWhenMissing;
    }

    /// <summary>Whether <paramref name="flag"/> is on, or the explicit <paramref name="defaultValue"/> if it is unknown.</summary>
    /// <param name="flag">The flag name (case-insensitive).</param>
    /// <param name="defaultValue">The value to return when the flag is not present.</param>
    public bool IsEnabled(string flag, bool defaultValue)
    {
        System.ArgumentException.ThrowIfNullOrEmpty(flag);
        return flags.TryGetValue(flag, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Whether <paramref name="flag"/> is actually configured in this snapshot. <see cref="IsEnabled(string)"/>
    /// cannot distinguish a flag configured <c>false</c> from one that was never defined — both read
    /// <c>false</c> under the default <see cref="OrionFlagOptions.DefaultWhenMissing"/> — so a mistyped
    /// kill-switch name looks exactly like "the feature is off". Ask this to tell them apart.
    /// </summary>
    /// <param name="flag">The flag name (case-insensitive).</param>
    /// <returns><c>true</c> when the flag is present, whatever its value.</returns>
    public bool IsDefined(string flag)
    {
        System.ArgumentException.ThrowIfNullOrEmpty(flag);
        return flags.ContainsKey(flag);
    }

    /// <summary>The number of flags in this snapshot.</summary>
    public int Count => flags.Count;

    internal static FlagSnapshot From(IReadOnlyDictionary<string, bool> source, bool defaultWhenMissing)
    {
        var frozen = source.ToFrozenDictionary(System.StringComparer.OrdinalIgnoreCase);
        return new FlagSnapshot(frozen, defaultWhenMissing);
    }
}
