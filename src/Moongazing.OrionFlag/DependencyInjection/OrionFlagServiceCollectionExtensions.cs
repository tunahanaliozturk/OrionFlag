namespace Moongazing.OrionFlag.DependencyInjection;

using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moongazing.OrionFlag.Diagnostics;

/// <summary>
/// DI wiring for feature flags.
/// </summary>
public static class OrionFlagServiceCollectionExtensions
{
    /// <summary>
    /// Register the in-memory flag evaluator, the shared <see cref="FlagDiagnostics"/>, and the
    /// options. Configure flags in code here, and/or bind them from configuration with
    /// <c>services.Configure&lt;OrionFlagOptions&gt;(Configuration.GetSection("OrionFlags"))</c> — the
    /// evaluator picks up reloads through <c>IOptionsMonitor</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional in-code flag configuration.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddOrionFlag(this IServiceCollection services, Action<OrionFlagOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<OrionFlagOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<FlagDiagnostics>();
        services.TryAddSingleton<IOrionFlags, InMemoryOrionFlags>();

        return services;
    }
}
