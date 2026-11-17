using Jellyfin.Plugin.JellyBridge.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Utils;

/// <summary>
/// Runs one pipeline step (sync, favorite requests, cleanup, sort) once per enabled Discover library.
/// </summary>
public static class LibraryLoop
{
    /// <summary>
    /// Runs <paramref name="body"/> for every enabled library, inside <see cref="LibraryScope.Enter"/>, after making
    /// sure the library's folder exists. A library that throws is logged and reported through
    /// <paramref name="onError"/>, so one broken library fails the overall step without stopping the others.
    /// </summary>
    /// <param name="logger">Logger for per-library failures.</param>
    /// <param name="stepName">Name of the step, for the log message.</param>
    /// <param name="body">The step, which sees the library's settings and folder through the scope.</param>
    /// <param name="onError">Builds the failed result reported for a library whose step threw.</param>
    /// <returns>One entry per enabled library, with the result it produced or the reported failure.</returns>
    public static async Task<List<(DiscoverLibraryConfig Library, T Result)>> ForEachLibraryAsync<T>(
        ILogger logger,
        string stepName,
        Func<DiscoverLibraryConfig, Task<T>> body,
        Func<DiscoverLibraryConfig, Exception, T> onError)
    {
        var results = new List<(DiscoverLibraryConfig, T)>();

        foreach (var library in DiscoverLibraries.GetEnabled())
        {
            try
            {
                Directory.CreateDirectory(library.GetDirectory(DiscoverLibraries.GetRootDirectory()));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not create the folder of library '{Library}'; reporting the {Step} as failed for it", library.Name, stepName);
                results.Add((library, onError(library, ex)));
                continue;
            }

            try
            {
                using (LibraryScope.Enter(library))
                {
                    results.Add((library, await body(library)));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during {Step} for library {Library}: {Error}", stepName, library.Name, ex.Message);
                results.Add((library, onError(library, ex)));
            }
        }

        return results;
    }
}
