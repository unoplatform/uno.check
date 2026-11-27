#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NuGet.Configuration;

namespace DotNetCheck.DotNet;

/// <summary>
/// The NuGet configuration that applies to the directory uno-check runs from.
/// </summary>
/// <param name="Sources">The enabled package sources, in configuration order.</param>
/// <param name="GlobalPackagesFolder">The global packages folder the configuration selects.</param>
/// <param name="DisabledSources">The sources the configuration explicitly disables, which must stay disabled.</param>
/// <param name="ConnectionAttributes">
/// Connection settings of the enabled sources (by source name) that the NuGet version uno-check references does not expose.
/// </param>
internal sealed record NuGetConfiguration(
    IReadOnlyList<PackageSource> Sources,
    string GlobalPackagesFolder,
    IReadOnlyList<PackageSource> DisabledSources,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ConnectionAttributes);

/// <summary>
/// Resolves package sources through NuGet's own settings (nuget.config files from the working directory upward,
/// then the user and machine level settings), so uno-check sees the same feeds as the rest of the .NET tooling.
/// </summary>
internal static class NuGetSourceResolver
{
    private const string DefaultSourceUrl = "https://api.nuget.org/v3/index.json";

    // Per-source settings that change how a feed is reached, not who is allowed to use it.
    private static readonly string[] PreservedAttributes = ["allowInsecureConnections", "disableTLSCertificateValidation"];

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
        var all = new PackageSourceProvider(settings).LoadPackageSources().ToArray();
        var enabled = all.Where(s => s.IsEnabled).ToArray();

        return new NuGetConfiguration(
            enabled,
            SettingsUtility.GetGlobalPackagesFolder(settings),
            all.Where(s => !s.IsEnabled).ToArray(),
            ReadConnectionAttributes(settings, enabled));
    }

    /// <summary>
    /// Adds the manifest's sources to the configured ones without duplicating a configured source. The public
    /// nuget.org feed is only kept when the configuration already lists it, so a manifest never forces it on a
    /// configuration that doesn't use it, and a source the configuration disables is not added back.
    /// </summary>
    internal static IReadOnlyList<PackageSource> MergeManifestSources(
        IReadOnlyList<PackageSource> configured,
        IEnumerable<string>? manifestSources,
        IEnumerable<PackageSource>? disabled = null)
    {
        var merged = new List<PackageSource>(configured);
        var seen = new HashSet<string>(configured.Select(s => Normalize(s.Source)), StringComparer.Ordinal);
        var disabledSources = new HashSet<string>((disabled ?? []).Select(s => Normalize(s.Source)), StringComparer.Ordinal);

        foreach (var manifestSource in manifestSources ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(manifestSource) || IsPublicNuGetOrg(manifestSource))
                continue;

            var normalized = Normalize(manifestSource);
            if (!disabledSources.Contains(normalized) && seen.Add(normalized))
                merged.Add(new PackageSource(manifestSource.Trim()));
        }

        return merged;
    }

    internal static bool IsPublicNuGetOrg(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Host.Equals("nuget.org", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".nuget.org", StringComparison.OrdinalIgnoreCase));

    // HTTP(S) sources compare by scheme, host and port without regard to case, but their path is case-sensitive.
    // Local paths are only case-insensitive on Windows.
    internal static string Normalize(string source)
    {
        source = source.Trim();

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}{uri.Query}";
        }

        var path = source.TrimEnd('/', '\\');
        return Util.IsWindows ? path.Replace('/', '\\').ToLowerInvariant() : path;
    }

    // The NuGet version uno-check references drops these attributes when it loads sources, so they are read
    // from the files directly. The first file that defines a source wins, as the closest configuration does.
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ReadConnectionAttributes(
        ISettings settings,
        IReadOnlyList<PackageSource> sources)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var pending = new HashSet<string>(sources.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var path in settings.GetConfigFilePaths())
        {
            if (pending.Count == 0)
                break;

            try
            {
                var elements = XDocument.Load(path).Root?.Element("packageSources")?.Elements().ToList() ?? new List<XElement>();

                // Only the entries after the last <clear /> are in effect, and NuGet keeps the first entry of a repeated key.
                var lastClear = elements.FindLastIndex(e => e.Name.LocalName.Equals("clear", StringComparison.OrdinalIgnoreCase));
                var effective = elements.Skip(lastClear + 1)
                    .Where(e => e.Name.LocalName.Equals("add", StringComparison.OrdinalIgnoreCase));

                foreach (var entry in effective)
                {
                    var key = (string?)entry.Attribute("key");
                    if (key is null || !pending.Remove(key))
                        continue;

                    var preserved = PreservedAttributes
                        .Select(name => entry.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        .Where(a => a is not null)
                        .ToDictionary(a => a!.Name.LocalName, a => a!.Value);

                    if (preserved.Count > 0)
                        result[key] = preserved;
                }
            }
            catch (Exception ex)
            {
                Util.Log($"Could not read the package sources of '{path}': {ex.Message}");
            }
        }

        return result;
    }
}
