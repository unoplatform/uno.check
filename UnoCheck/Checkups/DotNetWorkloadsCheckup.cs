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
		private bool _skipMaui = false;

        public DotNetWorkloadsCheckup() : base()
		{
			throw new Exception("Do not IOC this type directly");
		}

		public DotNetWorkloadsCheckup(SharedState sharedState, string sdkVersion, Manifest.DotNetWorkload[] requiredWorkloads, params string[] nugetPackageSources) : base()
		{
			var dotnet = new DotNetSdk(sharedState);

			SdkRoot = dotnet.DotNetSdkLocation.FullName;
			SdkVersion = sdkVersion;
			if (sharedState.TryGetState<TargetPlatform>(StateKey.EntryPoint, StateKey.TargetPlatforms, out var activeTargetPlatforms))
			{
				TargetPlatforms = activeTargetPlatforms;
			}

			if (sharedState.TryGetState<SkipInfo[]>(StateKey.EntryPoint, StateKey.Skips, out var skips))
			{
				_skipMaui = skips.Any(s => s.CheckupId == "maui");
			}

			RequiredWorkloads = requiredWorkloads.Where(FilterPlatform).ToArray();
			NuGetPackageSources = nugetPackageSources;
		}

		private bool FilterPlatform(Manifest.DotNetWorkload w)
		{
			var arch = Util.IsArm64 ? "arm64" : "x64";
			var targetPlatform = Util.Platform + "/" + arch;

			if (w.SupportedPlatforms?.Any(sp => sp == (sp.Contains("/") ? targetPlatform : Util.Platform.ToString())) ?? false)
			{
				switch (w.Id)
				{
					case "android" when TargetPlatforms.HasFlag(TargetPlatform.Android):
					case "maui-android" when !_skipMaui && TargetPlatforms.HasFlag(TargetPlatform.Android):
					case "maui" when !_skipMaui && (TargetPlatforms.HasFlag(TargetPlatform.Android)
									|| TargetPlatforms.HasFlag(TargetPlatform.iOS)
									|| TargetPlatforms.HasFlag(TargetPlatform.macOS)):
					case "ios" when TargetPlatforms.HasFlag(TargetPlatform.iOS):
					case "tvos" when TargetPlatforms.HasFlag(TargetPlatform.tvOS):
					case "maccatalyst" when TargetPlatforms.HasFlag(TargetPlatform.macOS):
					case "wasm-tools" when TargetPlatforms.HasFlag(TargetPlatform.WebAssembly):
						return true;
				}

			}

			return false;
		}

		public readonly string SdkRoot;
		public readonly string SdkVersion;
		public readonly string[] NuGetPackageSources;
		public readonly Manifest.DotNetWorkload[] RequiredWorkloads;
		public readonly TargetPlatform TargetPlatforms;

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
			var installedWorkloads = await manager.GetInstalledWorkloads();
			var availablePackageWorkloads = await manager.GetAvailableWorkloads();

			foreach (var rp in RequiredWorkloads)
			{
				var versionParts = rp.Version.Split("/", StringSplitOptions.None);
				var workloadVersion = versionParts.First();
				var workloadSdkVersion = versionParts.ElementAtOrDefault(1) is { Length: > 0 } v ? v : sdkVersion;


				if (!NuGetVersion.TryParse(workloadVersion, out var rpVersion))
					rpVersion = new NuGetVersion(0, 0, 0);

				if (Util.Verbose)
				{
					foreach (var installedWorload in availablePackageWorkloads)
					{
						ReportStatus($"Available workload: {installedWorload.id}: {installedWorload.version}", null);
					}
				}

				if (
					availablePackageWorkloads.FirstOrDefault(ip => ip.id.Equals(rp.WorkloadManifestId, StringComparison.OrdinalIgnoreCase) && NuGetVersion.TryParse(ip.version, out var ipVersion) && ipVersion >= rpVersion) is { id: not null } available
					&& installedWorkloads.Contains(rp.Id)
				)
				{
					ReportStatus($"{available.id} ({available.version}/{available.sdkVersion}) is installed.", Status.Ok);
				}
				else
				{
					ReportStatus($"{rp.Id} ({rp.PackageId} : {rp.Version}) is not installed.", Status.Error);
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
					history.ContributeState(StateKey.EntryPoint, "ShouldRestartVS", true);
				})));
		}
	}
}
