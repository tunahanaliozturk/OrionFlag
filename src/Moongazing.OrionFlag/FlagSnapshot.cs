namespace Moongazing.OrionFlag;

using System;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using Moongazing.OrionFlag.Diagnostics;

/// <summary>
/// An immutable, point-in-time view of every flag. Capturing a snapshot at the start of a request and
/// querying it throughout guarantees a flag cannot flip mid-request even if the underlying config
/// reloads. Lookups are lock-free and allocation-free (a <see cref="FrozenDictionary{TKey, TValue}"/>
/// read), so the hot path stays cheap.
/// </summary>
public sealed class FlagSnapshot
{
    private readonly FrozenDictionary<string, FlagValue> flags;
    private readonly FrozenDictionary<string, RolloutValue> rollouts;
    private readonly bool defaultWhenMissing;
    private readonly FlagDiagnostics diagnostics;

    internal FlagSnapshot(FrozenDictionary<string, FlagValue> flags, FrozenDictionary<string, RolloutValue> rollouts, bool defaultWhenMissing, FlagDiagnostics diagnostics)
    {
        this.flags = flags;
        this.rollouts = rollouts;
        this.defaultWhenMissing = defaultWhenMissing;
        this.diagnostics = diagnostics;
    }

    /// <summary>Whether <paramref name="flag"/> is on in this snapshot, or the snapshot default if it is unknown.</summary>
    /// <param name="flag">The flag name (case-insensitive).</param>
    public bool IsEnabled(string flag)
    {
        System.ArgumentException.ThrowIfNullOrEmpty(flag);
        var result = Evaluate(flag, out var canonicalName);
        diagnostics.RecordEvaluation(canonicalName ?? FlagDiagnostics.UndefinedFlagTagValue, result, canonicalName is not null);
        return result;
    }

    /// <summary>Whether <paramref name="flag"/> is on, or the explicit <paramref name="defaultValue"/> if it is unknown.</summary>
    /// <param name="flag">The flag name (case-insensitive).</param>
    /// <param name="defaultValue">The value to return when the flag is not present.</param>
    public bool IsEnabled(string flag, bool defaultValue)
    {
        System.ArgumentException.ThrowIfNullOrEmpty(flag);
        var result = Evaluate(flag, defaultValue, out var canonicalName);
        diagnostics.RecordEvaluation(canonicalName ?? FlagDiagnostics.UndefinedFlagTagValue, result, canonicalName is not null);
        return result;
    }

    /// <summary>
    /// Evaluate a flag for a stable subject identity. A configured <c>false</c> always wins;
    /// an enabled flag without a rollout serves <c>true</c>. Unknown flags use the snapshot default.
    /// The subject is used only for local hashing and is never recorded in metric tags.
    /// </summary>
    public bool IsEnabledFor(string flag, string subject)
    {
        ArgumentException.ThrowIfNullOrEmpty(flag);
        ArgumentException.ThrowIfNullOrEmpty(subject);

        bool result;
        string? canonicalName;
        if (flags.TryGetValue(flag, out var value))
        {
            canonicalName = value.Name;
            result = value.Enabled && (!rollouts.TryGetValue(flag, out var rollout)
                || InRollout(value.Name, subject, rollout));
        }
        else
        {
            canonicalName = null;
            result = defaultWhenMissing;
        }

        diagnostics.RecordEvaluation(canonicalName ?? FlagDiagnostics.UndefinedFlagTagValue, result, canonicalName is not null);
        return result;
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

    internal static FlagSnapshot From(
        IReadOnlyDictionary<string, bool> source,
        IReadOnlyDictionary<string, PercentageRolloutOptions> rolloutSource,
        bool defaultWhenMissing,
        FlagDiagnostics diagnostics)
    {
        var frozen = source.ToFrozenDictionary(
            static pair => pair.Key,
            static pair => new FlagValue(pair.Value, pair.Key),
            StringComparer.OrdinalIgnoreCase);
        var validated = new Dictionary<string, RolloutValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, rollout) in rolloutSource)
        {
            if (!frozen.ContainsKey(name))
            {
                throw new ArgumentException($"Rollout '{name}' has no corresponding flag.", nameof(rolloutSource));
            }

            if (rollout is null || rollout.Percentage is < 0 or > 100 || rollout.Salt is null)
            {
                throw new ArgumentException($"Rollout '{name}' requires a percentage from 0 to 100 and a non-null salt.", nameof(rolloutSource));
            }

            validated.Add(name, new RolloutValue(rollout.Percentage, rollout.Salt));
        }

        return new FlagSnapshot(frozen, validated.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), defaultWhenMissing, diagnostics);
    }

    private static bool InRollout(string name, string subject, RolloutValue rollout)
    {
        if (rollout.Percentage == 0)
        {
            return false;
        }

        if (rollout.Percentage == 100)
        {
            return true;
        }

        // Length-prefix each UTF-8 component so distinct triples cannot alias. SHA-256 is stable
        // across processes and runtime versions; string.GetHashCode is intentionally not.
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var saltBytes = Encoding.UTF8.GetBytes(rollout.Salt);
        var subjectBytes = Encoding.UTF8.GetBytes(subject);
        var input = new byte[checked(12 + nameBytes.Length + saltBytes.Length + subjectBytes.Length)];
        var offset = 0;
        WritePart(nameBytes);
        WritePart(saltBytes);
        WritePart(subjectBytes);
        var digest = SHA256.HashData(input);
        var bucket = BinaryPrimitives.ReadUInt64BigEndian(digest) % 10_000;
        return bucket < (uint)(rollout.Percentage * 100);

        void WritePart(byte[] part)
        {
            BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(offset, 4), part.Length);
            offset += 4;
            part.CopyTo(input, offset);
            offset += part.Length;
        }
    }

    internal readonly record struct FlagValue(bool Enabled, string Name);

    internal readonly record struct RolloutValue(int Percentage, string Salt);
}
