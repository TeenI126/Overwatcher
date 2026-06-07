using Microsoft.Extensions.DependencyInjection;
using OwTracker.Core.Services.Interfaces;

namespace OwTracker.ML;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the ML layer. Currently the stub classifier; the roster provider is real.
    /// </summary>
    public static IServiceCollection AddOwTrackerMl(this IServiceCollection services)
    {
        services.AddSingleton<IHeroRosterProvider, HeroRosterProvider>();
        services.AddSingleton<IHeroClassifier, StubHeroClassifier>();
        return services;
    }
}
