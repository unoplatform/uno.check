using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.DotNet;

internal static class NuGetHelper
{
    public static async Task<NuGetVersion> GetLatestPackageVersionAsync(string packageName, bool includePrerelease)
    {
        var source = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");

        // check for a newer version
        var packageMetadataResource = await source.GetResourceAsync<PackageMetadataResource>();
        var packageMetadata = await packageMetadataResource.GetMetadataAsync(
            packageName,
            includePrerelease: includePrerelease,
            includeUnlisted: false,
            new SourceCacheContext(),
            NullLogger.Instance,
            CancellationToken.None);
        var latestVersion = packageMetadata.Select(p => p.Identity.Version).Max();
        return latestVersion;
    }
}
