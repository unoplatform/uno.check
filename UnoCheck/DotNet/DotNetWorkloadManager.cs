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
using System.Text.Json;

namespace DotNetCheck.DotNet
{
	public class DotNetWorkloadManager
	{
		public DotNetWorkloadManager(string sdkRoot, string sdkVersion, params string[] nugetPackageSources)
		{
			SdkRoot = sdkRoot;
			SdkVersion = sdkVersion;
			NuGetPackageSources = nugetPackageSources;

			DotNetCliWorkingDir = Path.Combine(Path.GetTempPath(), "uno-check-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
			await CliRepair();
		}

		public async Task Install(Manifest.DotNetWorkload[] workloads)
		{
			var rollbackFile = WriteRollbackFile(workloads);

			await CliInstallWithRollback(rollbackFile, workloads.Where(w => !w.Abstract).Select(w => w.Id));
		}

		string WriteRollbackFile(Manifest.DotNetWorkload[] workloads)
		{
			var workloadRollback = new Dictionary<string, string>();

			foreach (var workload in workloads)
				workloadRollback[workload.WorkloadManifestId] = workload.Version;

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

        const string RollbackOutputBeginMarker = "==workloadRollbackDefinitionJsonOutputStart==";
        const string RollbackOutputEndMarker = "==workloadRollbackDefinitionJsonOutputEnd==";

        public async Task<(string id, string version, string sdkVersion)[]> GetInstalledWorkloads()
        {
            var dotnetExe = Path.Combine(SdkRoot, DotNetSdk.DotNetExeName);

            var args = new List<string>
            {
                "workload",
                "update",
                "--print-rollback"
            };

            var r = await Util.WrapShellCommandWithSudo(dotnetExe, DotNetCliWorkingDir, true, args.ToArray());

            // Throw if this failed with a bad exit code
            if (r.ExitCode != 0)
                throw new Exception("Workload command failed: `dotnet " + string.Join(' ', args) + "`");

			var output = string.Join(" ", r.StandardOutput);
			var startIndex = output.IndexOf(RollbackOutputBeginMarker);
			var endIndex = output.IndexOf(RollbackOutputEndMarker);

			if(startIndex >= 0 && endIndex >= 0)
			{
				var start = startIndex + RollbackOutputBeginMarker.Length;
				var json = output.Substring(start, endIndex - start);

				var workloads = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

				return workloads
					.Select(p => {
                        var versionParts = p.Value.Split("/", StringSplitOptions.None);
                        var workloadVersion = versionParts.First();
						var workloadSdkVersion = versionParts.ElementAtOrDefault(1) is { Length: > 0 } v ? v : "";

                        return (p.Key, workloadVersion, workloadSdkVersion);
					})
					.ToArray();
			}
			else
			{
                throw new Exception("Workload command output cannot be parsed: `dotnet " + string.Join(' ', args) + "`");
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
