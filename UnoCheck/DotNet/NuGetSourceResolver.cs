#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuGet.Configuration;

namespace DotNetCheck.DotNet;

/// <summary>
/// The NuGet configuration that applies to the directory uno-check runs from.
/// </summary>
/// <param name="Sources">The enabled package sources, in configuration order.</param>
/// <param name="GlobalPackagesFolder">The global packages folder the configuration selects.</param>
internal sealed record NuGetConfiguration(IReadOnlyList<PackageSource> Sources, string GlobalPackagesFolder);

/// <summary>
/// Resolves package sources through NuGet's own settings (nuget.config files from the working directory upward,
/// then the user and machine level settings), so uno-check sees the same feeds as the rest of the .NET tooling.
/// </summary>
internal static class NuGetSourceResolver
{
    private const string DefaultSourceUrl = "https://api.nuget.org/v3/index.json";

    private static readonly Lazy<NuGetConfiguration?> current = new(() => TryLoad(Directory.GetCurrentDirectory()));

    /// <summary>
    /// The configuration for the current directory, loaded once per run so every lookup uses the same sources.
    /// <see langword="null"/> when the configuration cannot be read.
    /// </summary>
    public static NuGetConfiguration? Current => current.Value;

    /// <summary>
    /// The sources an optional latest-version lookup should query. An unreadable configuration falls back to the
    /// default public source, as before; a readable one with no enabled source yields none, so the lookup is skipped.
    /// </summary>
    public static IReadOnlyList<PackageSource> GetLookupSources(NuGetConfiguration? configuration)
        => configuration?.Sources ?? new[] { new PackageSource(DefaultSourceUrl, "nuget.org") };

    internal static NuGetConfiguration? TryLoad(string workingDirectory)
    {
        try
        {
            return Load(Settings.LoadDefaultSettings(workingDirectory));
        }
        catch (Exception ex)
        {
            // Reading the configuration is best effort: callers fall back to their previous behavior.
            Util.Log($"Could not read the NuGet configuration for '{workingDirectory}': {ex.Message}");
            return null;
        }
    }

    internal static NuGetConfiguration Load(ISettings settings)
    {
        var sources = new PackageSourceProvider(settings)
            .LoadPackageSources()
            .Where(s => s.IsEnabled)
            .ToArray();

        return new NuGetConfiguration(sources, SettingsUtility.GetGlobalPackagesFolder(settings));
    }

    /// <summary>
    /// Adds the manifest's sources to the configured ones without duplicating a configured source. The public
    /// nuget.org feed is only kept when the configuration already lists it, so a manifest never forces it on a
    /// configuration that doesn't use it.
    /// </summary>
    internal static IReadOnlyList<PackageSource> MergeManifestSources(
        IReadOnlyList<PackageSource> configured,
        IEnumerable<string>? manifestSources)
    {
        var merged = new List<PackageSource>(configured);
        var seen = new HashSet<string>(configured.Select(s => Normalize(s.Source)), StringComparer.OrdinalIgnoreCase);

        foreach (var manifestSource in manifestSources ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(manifestSource) || IsPublicNuGetOrg(manifestSource))
                continue;

            if (seen.Add(Normalize(manifestSource)))
                merged.Add(new PackageSource(manifestSource.Trim()));
        }

        return merged;
    }

    internal static bool IsPublicNuGetOrg(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Host.Equals("nuget.org", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".nuget.org", StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string source) => source.Trim().TrimEnd('/');
}
