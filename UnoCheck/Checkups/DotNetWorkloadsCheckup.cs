using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotNetCheck.DotNet;
using DotNetCheck.Models;
using DotNetCheck.Solutions;
using Microsoft.NET.Sdk.WorkloadManifestReader;
using NuGet.Versioning;

namespace DotNetCheck.Checkups
{
	public class DotNetWorkloadsCheckup
		: Checkup
	{
		public DotNetWorkloadsCheckup() : base()
		{
			throw new Exception("Do not IOC this type directly");
		}

		public DotNetWorkloadsCheckup(SharedState sharedState, string sdkVersion, Manifest.DotNetWorkload[] requiredWorkloads, params string[] nugetPackageSources) : base()
		{
			var dotnet = new DotNetSdk(sharedState);

			SdkRoot = dotnet.DotNetSdkLocation.FullName;
			SdkVersion = sdkVersion;
			RequiredWorkloads = requiredWorkloads.Where(FilterPlatform).ToArray();
			NuGetPackageSources = nugetPackageSources;
		}

		private static bool FilterPlatform(Manifest.DotNetWorkload w)
		{
			var arch = Util.IsArm64 ? "arm64" : "x64";
			var targetPlatform = Util.Platform + "/" + arch;

			return w.SupportedPlatforms?.Any(sp => sp == (sp.Contains("/") ? Util.Platform.ToString() : targetPlatform)) ?? false;
		}

		public readonly string SdkRoot;
		public readonly string SdkVersion;
		public readonly string[] NuGetPackageSources;
		public readonly Manifest.DotNetWorkload[] RequiredWorkloads;

		public override IEnumerable<CheckupDependency> DeclareDependencies(IEnumerable<string> checkupIds)
			=> new[] { new CheckupDependency("dotnet") };

		public override string Id => "dotnetworkloads-" + SdkVersion;

		public override string Title => $".NET SDK - Workloads ({SdkVersion})";

		static bool wasForceRunAlready = false;

		public override async Task<DiagnosticResult> Examine(SharedState history)
		{
			if (!history.TryGetEnvironmentVariable("DOTNET_SDK_VERSION", out var sdkVersion))
				sdkVersion = SdkVersion;

			var force = history.TryGetEnvironmentVariable("DOTNET_FORCE", out var forceDotNet)
				&& (forceDotNet?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false)
				&& !wasForceRunAlready;

			// Don't allow multiple force runs, just the first
			if (force)
				wasForceRunAlready = true;


			var validWorkloads = RequiredWorkloads
				.Where(w => w.SupportedPlatforms?.Contains(Util.Platform) ?? false)
				.ToArray();

			var workloadManagers = validWorkloads
				.Select(w => w.Version.Split("/", StringSplitOptions.None).LastOrDefault() is { Length: > 0 } workloadSdkVersion ? workloadSdkVersion : sdkVersion)
				.Concat(new[] { sdkVersion })
				.Distinct()
				.ToDictionary(v => v, v => new DotNetWorkloadManager(SdkRoot, v, NuGetPackageSources));

			var missingWorkloads = new List<Manifest.DotNetWorkload>();

			foreach (var rp in RequiredWorkloads)
			{
				var versionParts = rp.Version.Split("/", StringSplitOptions.None);
				var workloadVersion = versionParts.First();
				var workloadSdkVersion = versionParts.ElementAtOrDefault(1) is { Length: > 0 } v ? v : sdkVersion;

				if (!workloadManagers.TryGetValue(workloadSdkVersion, out var workloadManager))
				{
					throw new Exception($"Unable to find workload manager for version [{rp.Id}: {rp.Version}]");
				}

				var installedPackageWorkloads = workloadManager.GetInstalledWorkloads();

				if (!NuGetVersion.TryParse(workloadVersion, out var rpVersion))
					rpVersion = new NuGetVersion(0, 0, 0);

#if DEBUG
				foreach (var installedWorload in installedPackageWorkloads)
				{
					ReportStatus($"Reported installed: {installedWorload.id}: {installedWorload.version}", null);
				}
#endif

				// TODO: Eventually check actual workload resolver api for installed workloads and
				// compare the manifest version once it has a string in it
				if (!installedPackageWorkloads.Any(ip => ip.id.Equals(rp.Id, StringComparison.OrdinalIgnoreCase) && NuGetVersion.TryParse(ip.version, out var ipVersion) && ipVersion == rpVersion))
				{
					ReportStatus($"{rp.Id} ({rp.PackageId} : {rp.Version}) not installed.", Status.Error);
					missingWorkloads.Add(rp);
				}
				else
				{
					ReportStatus($"{rp.Id} ({rp.PackageId} : {rp.Version}) installed.", Status.Ok);
				}
			}

			if (!missingWorkloads.Any() && !force)
				return DiagnosticResult.Ok(this);

			var genericWorkloadManager = new DotNetWorkloadManager(SdkRoot, sdkVersion, NuGetPackageSources);

			return new DiagnosticResult(
				Status.Error,
				this,
				new Suggestion("Install or Update SDK Workloads",
				new ActionSolution(async (sln, cancel) =>
				{
					if (history.GetEnvironmentVariableFlagSet("DOTNET_FORCE"))
					{
						try
						{
							await genericWorkloadManager.Repair();
						}
						catch (Exception ex)
						{
							ReportStatus("Warning: Workload repair failed", Status.Warning);
						}
					}

					await genericWorkloadManager.Install(RequiredWorkloads);
				})));
		}
	}
}
