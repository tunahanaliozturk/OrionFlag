namespace Moongazing.OrionFlag;

using System;
using System.Collections.Generic;

/// <summary>
/// The flag configuration: a map of flag name to on/off, plus the default served for an unknown flag.
/// Bind it from an <c>appsettings</c> section (the <c>IOptionsMonitor</c> bridge picks up reloads) or
/// configure it in code. Existing boolean feature settings migrate here with no code change.
/// </summary>
public sealed class OrionFlagOptions
{
    /// <summary>The flags, by name. Values reload live when bound to configuration.</summary>
    public Dictionary<string, bool> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The value served for a flag that is not present. Defaults to false (off).</summary>
    public bool DefaultWhenMissing { get; set; }
}
