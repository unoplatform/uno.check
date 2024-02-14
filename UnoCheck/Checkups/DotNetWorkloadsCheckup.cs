using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotNetCheck.DotNet;
using DotNetCheck.Models;
using DotNetCheck.Solutions;
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

			return w.SupportedPlatforms?.Any(sp => sp == (sp.Contains("/") ? targetPlatform : Util.Platform.ToString())) ?? false;
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
				.ToArray();

			var manager = new DotNetWorkloadManager(SdkRoot, SdkVersion, NuGetPackageSources);

			var missingWorkloads = new List<Manifest.DotNetWorkload>();
			var installedPackageWorkloads = await manager.GetInstalledWorkloads();

			foreach (var rp in RequiredWorkloads)
			{
				var versionParts = rp.Version.Split("/", StringSplitOptions.None);
				var workloadVersion = versionParts.First();
				var workloadSdkVersion = versionParts.ElementAtOrDefault(1) is { Length: > 0 } v ? v : sdkVersion;


				if (!NuGetVersion.TryParse(workloadVersion, out var rpVersion))
					rpVersion = new NuGetVersion(0, 0, 0);

#if DEBUG
				foreach (var installedWorload in installedPackageWorkloads)
				{
					ReportStatus($"Reported installed: {installedWorload.id}: {installedWorload.version}", null);
				}
#endif

				if (installedPackageWorkloads.FirstOrDefault(ip => ip.id.Equals(rp.WorkloadManifestId, StringComparison.OrdinalIgnoreCase) && NuGetVersion.TryParse(ip.version, out var ipVersion) && ipVersion >= rpVersion) is { id: not null } installed)
				{
                    ReportStatus($"{installed.id} ({installed.version}/{installed.sdkVersion}) installed.", Status.Ok);
				}
				else
				{
                    ReportStatus($"{rp.Id} ({rp.PackageId} : {rp.Version}) not installed.", Status.Error);
                    missingWorkloads.Add(rp);
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
