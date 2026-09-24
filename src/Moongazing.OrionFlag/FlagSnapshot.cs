namespace Moongazing.OrionFlag;

using System;
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
    private readonly FrozenDictionary<string, FlagValue> flags;
    private readonly bool defaultWhenMissing;

    internal FlagSnapshot(FrozenDictionary<string, FlagValue> flags, bool defaultWhenMissing)
    {
        this.flags = flags;
        this.defaultWhenMissing = defaultWhenMissing;
    }

    /// <summary>Whether <paramref name="flag"/> is on in this snapshot, or the snapshot default if it is unknown.</summary>
    /// <param name="flag">The flag name (case-insensitive).</param>
    public bool IsEnabled(string flag)
    {
        System.ArgumentException.ThrowIfNullOrEmpty(flag);
        return Evaluate(flag, out _);
    }

    /// <summary>Whether <paramref name="flag"/> is on, or the explicit <paramref name="defaultValue"/> if it is unknown.</summary>
    /// <param name="flag">The flag name (case-insensitive).</param>
    /// <param name="defaultValue">The value to return when the flag is not present.</param>
    public bool IsEnabled(string flag, bool defaultValue)
    {
        System.ArgumentException.ThrowIfNullOrEmpty(flag);
        return Evaluate(flag, defaultValue, out _);
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

    internal bool Evaluate(string flag, out string? canonicalName) =>
        Evaluate(flag, defaultWhenMissing, out canonicalName);

    internal bool Evaluate(string flag, bool defaultValue, out string? canonicalName)
    {
        if (flags.TryGetValue(flag, out var value))
        {
            canonicalName = value.Name;
            return value.Enabled;
        }

        canonicalName = null;
        return defaultValue;
    }

    internal static FlagSnapshot From(IReadOnlyDictionary<string, bool> source, bool defaultWhenMissing)
    {
        var frozen = source.ToFrozenDictionary(
            static pair => pair.Key,
            static pair => new FlagValue(pair.Value, pair.Key),
            StringComparer.OrdinalIgnoreCase);
        return new FlagSnapshot(frozen, defaultWhenMissing);
    }

    internal readonly record struct FlagValue(bool Enabled, string Name);
}
