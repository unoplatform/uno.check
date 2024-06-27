using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using DotNetCheck.Models;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DotNetCheck.Solutions
{
	internal class UnoSdkSolution : Solution
	{
		public override async Task Implement(SharedState state, CancellationToken ct)
		{
			var resource =
				await Repository
					.Factory
					.GetCoreV3("https://api.nuget.org/v3/index.json")
					.GetResourceAsync<FindPackageByIdResource>();

			state.TryGetState("unosdk", "unoSdkLatestVersion", out string unoSdkVersion);

			using var unoSdk = await GetArchiveForPackageAsync(resource, "Uno.Sdk", unoSdkVersion, ct);

			var unoVersion = GetUnoVersionFromUnoSdk(unoSdk);

			using var uno = await GetArchiveForPackageAsync(resource, "Uno.WinUI", unoVersion, ct);

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

			new ShellProcessRunner(new("dotnet", "restore") { Verbose = true, WorkingDirectory = tempPath }).WaitForExit();
		}

		private static async Task<ZipArchive> GetArchiveForPackageAsync(FindPackageByIdResource resource, string packageId, string packageVersion, CancellationToken ct)
		{
			var ms = new MemoryStream();

			await resource.CopyNupkgToStreamAsync(
				packageId,
				NuGetVersion.Parse(packageVersion),
				ms,
				cacheContext: new() { NoCache = true },
				NullLogger.Instance,
				ct);

			return new ZipArchive(ms);
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