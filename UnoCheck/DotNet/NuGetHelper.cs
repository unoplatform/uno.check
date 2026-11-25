#nullable enable

using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.DotNet;

internal static class NuGetHelper
{
    // Optional lookups must not stall a run on an unresponsive feed.
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The latest version of a package across the configured NuGet sources, or <see langword="null"/> when no source
    /// could provide one. A source that is unreachable only affects the lookup if no other source answers.
    /// </summary>
    public static Task<NuGetVersion?> GetLatestPackageVersionAsync(string packageName, bool includePrerelease, CancellationToken cancellationToken = default)
        => GetLatestPackageVersionAsync(
            NuGetSourceResolver.GetLookupSources(NuGetSourceResolver.Current),
            (source, ct) => GetVersionsAsync(source, packageName, includePrerelease, ct),
            LookupTimeout,
            cancellationToken);

    internal static async Task<NuGetVersion?> GetLatestPackageVersionAsync(
        IReadOnlyList<PackageSource> sources,
        Func<PackageSource, CancellationToken, Task<IEnumerable<NuGetVersion>>> getVersions,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (sources.Count == 0)
        {
            Util.Log("No enabled NuGet sources are configured; skipping the latest version lookup.");
            return null;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        var lookups = await Task.WhenAll(sources.Select(async source =>
        {
            try
            {
                return await getVersions(source, linked.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Util.Log($"Could not query the NuGet source '{source.Name}': {ex.Message}");
                return Enumerable.Empty<NuGetVersion>();
            }
        })).ConfigureAwait(false);

        return lookups.SelectMany(versions => versions).DefaultIfEmpty().Max();
    }

    private static async Task<IEnumerable<NuGetVersion>> GetVersionsAsync(
        PackageSource source,
        string packageName,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        var repository = Repository.Factory.GetCoreV3(source);
        var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken).ConfigureAwait(false);
        if (metadataResource is null)
            return Enumerable.Empty<NuGetVersion>();

        var metadata = await metadataResource.GetMetadataAsync(
            packageName,
            includePrerelease: includePrerelease,
            includeUnlisted: false,
            new SourceCacheContext(),
            NullLogger.Instance,
            cancellationToken).ConfigureAwait(false);

        return metadata.Select(p => p.Identity.Version).ToList();
    }
}
