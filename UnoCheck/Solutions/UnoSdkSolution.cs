using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using DotNetCheck.Models;
using DotNetCheck.DotNet;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DotNetCheck.Solutions
{
	internal class UnoSdkSolution : Solution
	{
		/// <summary>
		/// Restores the Uno.Sdk package through a scratch project in the temp directory and
		/// the per-user NuGet cache — no machine location is written.
		/// </summary>
		public override bool RequiresElevation => false;

		public override async Task Implement(SharedState state, CancellationToken ct)
		{
			var configuration = NuGetSourceResolver.Current;
			var sources = NuGetSourceResolver.GetLookupSources(configuration);

			state.TryGetState("unosdk", "unoSdkLatestVersion", out string unoSdkVersion);

			using var unoSdk = await GetArchiveForPackageAsync(sources, "Uno.Sdk", unoSdkVersion, ct);

			var unoVersion = GetUnoVersionFromUnoSdk(unoSdk);

			using var uno = await GetArchiveForPackageAsync(sources, "Uno.WinUI", unoVersion, ct);

			var tfm = GetCompatibleTfmForUno(uno);

			var csprojContents =
				$"""
				<Project Sdk="Uno.Sdk/{unoSdkVersion}">
					<PropertyGroup>
						<TargetFrameworks>{tfm}</TargetFrameworks>
					</PropertyGroup>
				</Project>
				""";

			var tempPath = Path.Combine(Path.GetTempPath(), $"Uno.Sdk-{Guid.NewGuid()}");

			Directory.CreateDirectory(tempPath);

			File.WriteAllText(Path.Combine(tempPath, "Uno.Sdk.csproj"), csprojContents);

			// The restore runs from this scratch directory, which would not see the nuget.config files of the
			// directory uno-check runs from. The packages folder is carried over too, so the package lands where
			// the Uno.Sdk check looks for it.
			if (configuration is { Sources.Count: > 0 })
				ScratchNuGetConfig.Write(tempPath, configuration.Sources, configuration.GlobalPackagesFolder, configuration.ConnectionAttributes);

			// A failed restore leaves the SDK unresolved; reporting the fix as applied would
			// hide that behind a check that still fails.
			Util.ThrowIfFailed(
				new ShellProcessRunner(new(DotNetSdk.ResolveExecutable(state), "restore") { Verbose = true, WorkingDirectory = tempPath }).WaitForExit(),
				"Restoring the Uno.Sdk package");
		}

		private static async Task<ZipArchive> GetArchiveForPackageAsync(IReadOnlyList<PackageSource> sources, string packageId, string packageVersion, CancellationToken ct)
		{
			var version = NuGetVersion.Parse(packageVersion);
			Exception lastError = null;

			foreach (var source in sources)
			{
				try
				{
					var resource = await Repository.Factory.GetCoreV3(source).GetResourceAsync<FindPackageByIdResource>(ct);
					var ms = new MemoryStream();

					if (resource is not null
						&& await resource.CopyNupkgToStreamAsync(packageId, version, ms, new() { NoCache = true }, NullLogger.Instance, ct))
					{
						ms.Position = 0;
						return new ZipArchive(ms);
					}
				}
				catch (Exception ex) when (!ct.IsCancellationRequested)
				{
					lastError = ex;
					Util.Log($"Could not download {packageId} {version} from the NuGet source '{source.Name}': {ex.Message}");
				}
			}

			throw lastError is null
				? new InvalidOperationException($"{packageId} {version} was not found in the configured NuGet sources.")
				: new InvalidOperationException($"{packageId} {version} could not be downloaded from the configured NuGet sources: {lastError.Message}", lastError);
		}

		private static string GetCompatibleTfmForUno(ZipArchive uno)
			=> uno.Entries
				  .First(e => Regex.IsMatch(e.FullName, "^lib/net\\d+\\.\\d/"))
				  .FullName
				  .Split('/', System.StringSplitOptions.RemoveEmptyEntries)[1];

		private static string GetUnoVersionFromUnoSdk(ZipArchive unoSdk)
		{
			using var sdkProps = unoSdk.OpenFile("Sdk/Sdk.props");

			var xml = new XmlDocument();
			xml.Load(sdkProps);

			return xml.SelectSingleNode("//*[name()='UnoVersion']").InnerText;
		}
	}
}