﻿using Microsoft.DotNet.MSBuildSdkResolver;
using Microsoft.NET.Sdk.WorkloadManifestReader;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Polly;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json.Linq;
using DotNetCheck.Models;

namespace DotNetCheck.DotNet
{
	public class DotNetWorkloadManager
	{
		public DotNetWorkloadManager(string sdkRoot, string sdkVersion, params string[] nugetPackageSources)
		{
			SdkRoot = sdkRoot;
			SdkVersion = sdkVersion;
			NuGetPackageSources = nugetPackageSources;

			CleanEmptyWorkloadDirectories(sdkRoot, sdkVersion);

			DotNetCliWorkingDir = Path.Combine(Path.GetTempPath(), "maui-check-" + Guid.NewGuid().ToString("N").Substring(0, 8));
			Directory.CreateDirectory(DotNetCliWorkingDir);

			var globalJson = new DotNetGlobalJson();
			globalJson.Sdk.Version = sdkVersion;
			globalJson.Sdk.RollForward = "disable";
			globalJson.Sdk.AllowPrerelease = true;
			File.WriteAllText(Path.Combine(DotNetCliWorkingDir, "global.json"), globalJson.ToJson());
		}

		public readonly string SdkRoot;
		public readonly string SdkVersion;

		public readonly string[] NuGetPackageSources;

		readonly string DotNetCliWorkingDir;

		public async Task Repair()
		{
			RemoveOldMetadata();

			await CliRepair();
		}

		public async Task Install(Manifest.DotNetWorkload[] workloads)
		{
			var rollbackFile = WriteRollbackFile(workloads);

			RemoveOldMetadata();

			await CliInstallWithRollback(rollbackFile, workloads.Where(w => !w.Abstract).Select(w => w.Id));
		}

		string WriteRollbackFile(Manifest.DotNetWorkload[] workloads)
		{
			var workloadRolback = GetInstalledWorkloadManifestIdsAndVersions();

			foreach (var workload in workloads)
				workloadRolback[workload.WorkloadManifestId] = workload.Version;

			var json = new StringBuilder();
			json.AppendLine("{");
			json.AppendLine(string.Join("," + Environment.NewLine,
				workloads.Select(wl => $"    \"{wl.WorkloadManifestId}\": \"{wl.Version}\"")));
			json.AppendLine("}");

			var rollbackFile = Path.Combine(DotNetCliWorkingDir, "workload.json");
			File.WriteAllText(rollbackFile, json.ToString());

			Util.Log($"Updating with Rollback File:" + Environment.NewLine + json.ToString());

			return rollbackFile;
		}

		Dictionary<string, string> GetInstalledWorkloadManifestIdsAndVersions()
		{
			var items = new Dictionary<string, string>();

			var manifestProvider = new SdkDirectoryWorkloadManifestProvider(SdkRoot, SdkVersion, null);

			foreach (var manifestInfo in manifestProvider.GetManifests())
			{
				using (var manifestStream = manifestInfo.OpenManifestStream())
				{
					var m = WorkloadManifestReader.ReadWorkloadManifest(manifestInfo.ManifestId, manifestStream, manifestInfo.ManifestPath);
					items[manifestInfo.ManifestId] = m.Version;
				}
			}

			return items;
		}

		public IEnumerable<(string id, string version)> GetInstalledWorkloads()
		{
			var manifestProvider = new SdkDirectoryWorkloadManifestProvider(SdkRoot, SdkVersion, null);

			foreach (var manifestInfo in manifestProvider.GetManifests())
			{
				using (var manifestStream = manifestInfo.OpenManifestStream())
				{
					var m = WorkloadManifestReader.ReadWorkloadManifest(manifestInfo.ManifestId, manifestStream, manifestInfo.ManifestPath);

					// Each workload manifest can have one or more workloads defined
					foreach (var wl in m.Workloads)
						yield return (wl.Key.ToString(), m.Version);
				}
			}
		}

		void RemoveOldMetadata()
		{
			var dir = GetInstalledWorkloadMetadataDir();

			var oldWorkloadIds = new [] {
				"microsoft-android-sdk-full",
				"microsoft-ios-sdk-full",
				"microsoft-maccatalyst-sdk-full",
				"microsoft-macos-sdk-full",
				"microsoft-tvos-sdk-full"
			};

			foreach (var owid in oldWorkloadIds)
			{
				try
				{
					var f = Path.Combine(dir, owid);
					if (File.Exists(f))
						File.Delete(f);
				}
				catch { }
			}
		}

		async Task CliInstallWithRollback(string rollbackFile, IEnumerable<string> workloadIds)
		{
			// dotnet workload install id --skip-manifest-update --add-source x
			var dotnetExe = Path.Combine(SdkRoot, DotNetSdk.DotNetExeName);

			// Arg switched to --source in >= preview 7
			var addSourceArg = "--source";
			if (NuGetVersion.Parse(SdkVersion) <= DotNetCheck.Manifest.DotNetSdk.Version6Preview6)
				addSourceArg = "--add-source";

			var args = new List<string>
			{
				"workload",
				"install",
				"--from-rollback-file",
				$"\"{rollbackFile}\""
			};
			args.AddRange(workloadIds);
			args.AddRange(NuGetPackageSources.Select(ps => $"{addSourceArg} \"{ps}\""));

			var r = await Util.WrapShellCommandWithSudo(dotnetExe, DotNetCliWorkingDir, true, args.ToArray());

			// Throw if this failed with a bad exit code
			if (r.ExitCode != 0)
				throw new Exception("Workload Install failed: `dotnet " + string.Join(' ', args) + "`");
		}

		async Task CliRepair()
		{
			// dotnet workload install id --skip-manifest-update --add-source x
			var dotnetExe = Path.Combine(SdkRoot, DotNetSdk.DotNetExeName);

			// Arg switched to --source in >= preview 7
			var addSourceArg = "--source";
			if (NuGetVersion.Parse(SdkVersion) <= DotNetCheck.Manifest.DotNetSdk.Version6Preview6)
				addSourceArg = "--add-source";

			var args = new List<string>
			{
				"workload",
				"repair"
			};
			args.AddRange(NuGetPackageSources.Select(ps => $"{addSourceArg} \"{ps}\""));

			var r = await Util.WrapShellCommandWithSudo(dotnetExe, DotNetCliWorkingDir, true, args.ToArray());

			// Throw if this failed with a bad exit code
			if (r.ExitCode != 0)
				throw new Exception("Workload Repair failed: `dotnet " + string.Join(' ', args) + "`");
		}

		void CleanEmptyWorkloadDirectories(string sdkRoot, string sdkVersion)
		{
			if (NuGetVersion.TryParse(sdkVersion, out var v))
			{
				var sdkBand = $"{v.Major}.{v.Minor}.{v.Patch}";

				var manifestsDir = Path.Combine(sdkRoot, "sdk-manifests", sdkBand);

				if (Directory.Exists(manifestsDir))
				{
					foreach (var dir in Directory.GetDirectories(manifestsDir))
					{
						var manifestFile = Path.Combine(dir, "WorkloadManifest.json");

						if (!File.Exists(manifestFile))
						{
							try { Util.Delete(dir, false); }
							catch { }
						}
					}
				}
			}
		}

		string GetInstalledWorkloadMetadataDir()
		{
			int last2DigitsTo0(int versionBuild)
				=> versionBuild / 100 * 100;

			if (!Version.TryParse(SdkVersion.Split('-')[0], out var result))
				throw new ArgumentException("Invalid 'SdkVersion' version: " + SdkVersion);

			var sdkVersionBand = $"{result.Major}.{result.Minor}.{last2DigitsTo0(result.Build)}";
			
			return Path.Combine(SdkRoot, "metadata", "workloads", sdkVersionBand, "InstalledWorkloads");
		}
	}
}
