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
		private record WorkloadListResult(string[] installed);

		public DotNetWorkloadManager(string sdkRoot, string sdkVersion, params string[] nugetPackageSources)
		{
			SdkRoot = sdkRoot;
			SdkVersion = sdkVersion;
			NuGetPackageSources = nugetPackageSources;

			DotNetCliWorkingDir = Path.Combine(Path.GetTempPath(), "uno-check-" + Guid.NewGuid().ToString("N").Substring(0, 8));
			Directory.CreateDirectory(DotNetCliWorkingDir);

			var globalJson = new DotNetGlobalJson();
			globalJson.Sdk.Version = sdkVersion;
			globalJson.Sdk.RollForward = "latestFeature";
			globalJson.Sdk.AllowPrerelease = true;
			File.WriteAllText(Path.Combine(DotNetCliWorkingDir, "global.json"), globalJson.ToJson());
		}

		public readonly string SdkRoot;
		public readonly string SdkVersion;

		public readonly string[] NuGetPackageSources;

		readonly string DotNetCliWorkingDir;

		public async Task Repair(CancellationToken cancellationToken = default)
		{
			await CliRepair(cancellationToken);
		}

		public async Task Install(Manifest.DotNetWorkload[] workloads, CancellationToken cancellationToken = default)
		{
			var rollbackFile = WriteRollbackFile(workloads);

			await CliInstallWithRollback(rollbackFile, workloads.Where(w => !w.Abstract).Select(w => w.Id), cancellationToken);
		}

		/// <summary>
		/// Pre-flight handshake before the install/repair spinner starts. On Linux/macOS,
		/// when the SDK directory isn't writable by the current user, a sudo password
		/// prompt would otherwise happen INSIDE the AnsiConsole.Status live spinner and
		/// be hidden from the user (see issue #515). Pre-caching sudo credentials here
		/// — while the plain console is still active — lets the in-spinner elevation use
		/// <c>sudo -n</c> against cached credentials and avoid prompting at all.
		/// </summary>
		public Task PrepareForInstallAsync(CancellationToken cancellationToken = default)
		{
			if (Util.IsWindows)
				return Task.CompletedTask;

			if (IsSdkPathWritable(SdkRoot))
				return Task.CompletedTask;

			if (Util.NonInteractive || Util.CI)
				return Task.CompletedTask;

			Util.Log($"SDK path '{SdkRoot}' is not writable by the current user; pre-caching sudo credentials before install.");
			return Util.EnsureSudoCredentialsCachedAsync(cancellationToken);
		}

		internal static string[] BuildInstallArgs(string sdkVersion, string rollbackFile, IEnumerable<string> workloadIds, IEnumerable<string> packageSources, bool verbose)
		{
			var addSourceArg = "--source";
			if (NuGetVersion.Parse(sdkVersion) <= DotNetCheck.Manifest.DotNetSdk.Version6Preview6)
				addSourceArg = "--add-source";

			var args = new List<string>
			{
				"workload",
				"install",
				"--from-rollback-file",
				$"\"{rollbackFile}\""
			};
			args.AddRange(workloadIds);
			args.AddRange(packageSources.Select(ps => $"{addSourceArg} \"{ps}\""));

			if (verbose)
			{
				args.Add("--verbosity");
				args.Add("detailed");
			}

			return args.ToArray();
		}

		internal static string[] BuildRepairArgs(string sdkVersion, IEnumerable<string> packageSources, bool verbose)
		{
			var addSourceArg = "--source";
			if (NuGetVersion.Parse(sdkVersion) <= DotNetCheck.Manifest.DotNetSdk.Version6Preview6)
				addSourceArg = "--add-source";

			var args = new List<string>
			{
				"workload",
				"repair"
			};
			args.AddRange(packageSources.Select(ps => $"{addSourceArg} \"{ps}\""));

			if (verbose)
			{
				args.Add("--verbosity");
				args.Add("detailed");
			}

			return args.ToArray();
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

		private (string begin, string end) RollbackOutputMarker = (
			"==workloadRollbackDefinitionJsonOutputStart==",
			"==workloadRollbackDefinitionJsonOutputEnd==");

		private (string begin, string end) ListOutputMarker = (
			"==workloadListJsonOutputStart==",
			"==workloadListJsonOutputEnd==");

		public async Task<string[]> GetInstalledWorkloads()
		{
			var dotnetExe = Path.Combine(SdkRoot, DotNetSdk.DotNetExeName);

			var args = new List<string>
			{
				"workload",
				"list",
				"--machine-readable"
			};

			var r = await Util.ShellCommand(dotnetExe, DotNetCliWorkingDir, Util.Verbose, args.ToArray());

			// Throw if this failed with a bad exit code
			if (r.ExitCode != 0)
				throw new Exception("Workload command failed: `dotnet " + string.Join(' ', args) + "`");

			var output = FilterWorkloadCommandOutput(string.Join(" ", r.StandardOutput), ListOutputMarker);

			// example of output {"installed":["wasm-tools"],"updateAvailable":[]}
			var workloads = JsonSerializer.Deserialize<WorkloadListResult>(output);
			if (workloads?.installed?.Length > 0)
			{
				return workloads.installed;
			}

			// On Linux/macOS, workload installs may have happened under elevated context.
			// Probe with `sudo -n` (no prompt) so checks don't get stuck waiting for credentials.
			if (!Util.IsWindows)
			{
				var sudoResult = await Util.WrapShellCommandWithSudoNoPrompt(dotnetExe, DotNetCliWorkingDir, Util.Verbose, args.ToArray());
				if (sudoResult.ExitCode == 0)
				{
					var sudoOutput = FilterWorkloadCommandOutput(string.Join(" ", sudoResult.StandardOutput), ListOutputMarker);
					var sudoWorkloads = JsonSerializer.Deserialize<WorkloadListResult>(sudoOutput);
					if (sudoWorkloads?.installed?.Length > 0)
					{
						return sudoWorkloads.installed;
					}
				}
			}

			return workloads?.installed ?? [];
		}

		public async Task<(string id, string version, string sdkVersion)[]> GetAvailableWorkloads()
		{
			var dotnetExe = Path.Combine(SdkRoot, DotNetSdk.DotNetExeName);

			var args = new List<string>
			{
				"workload",
				"update",
				"--print-rollback"
			};

			var r = await Util.ShellCommand(dotnetExe, DotNetCliWorkingDir, Util.Verbose, args.ToArray());

			// Throw if this failed with a bad exit code
			if (r.ExitCode != 0)
				throw new Exception("Workload command failed: `dotnet " + string.Join(' ', args) + "`");

			var output = FilterWorkloadCommandOutput(string.Join(" ", r.StandardOutput), RollbackOutputMarker);

			var workloads = JsonSerializer.Deserialize<Dictionary<string, string>>(output);

			return workloads
				.Select(p =>
				{
					var versionParts = p.Value.Split("/", StringSplitOptions.None);
					var workloadVersion = versionParts.First();
					var workloadSdkVersion = versionParts.ElementAtOrDefault(1) is { Length: > 0 } v ? v : "";

					return (p.Key, workloadVersion, workloadSdkVersion);
				})
				.ToArray();
		}

		async Task CliInstallWithRollback(string rollbackFile, IEnumerable<string> workloadIds, CancellationToken cancellationToken)
		{
			// dotnet workload install id --skip-manifest-update --add-source x
			var dotnetExe = Path.Combine(SdkRoot, DotNetSdk.DotNetExeName);

			var args = BuildInstallArgs(SdkVersion, rollbackFile, workloadIds, NuGetPackageSources, Util.Verbose);

			ShellProcessRunner.ShellProcessResult r;

			// On Linux/macOS, check if SDK path is writable before attempting user-level install.
			// If not writable, use sudo directly to avoid doomed user-level attempts that
			// fail with download/restore errors instead of clear permission-denied messages.
			if (!Util.IsWindows && !IsSdkPathWritable(SdkRoot))
			{
				Util.Log($"SDK path '{SdkRoot}' is not writable by the current user. Using elevated privileges.");
				r = await RetryWithSudo(dotnetExe, cancellationToken, args);
			}
			else
			{
				r = await Util.ShellCommand(dotnetExe, DotNetCliWorkingDir, Util.Verbose, cancellationToken, args);

				if (!Util.IsWindows && r.ExitCode != 0 && ShouldRetryWithSudo(r.GetOutput()))
				{
					r = await RetryWithSudo(dotnetExe, cancellationToken, args);
				}
			}

			if (cancellationToken.IsCancellationRequested)
				throw new OperationCanceledException(cancellationToken);

			// Throw if this failed with a bad exit code
			if (r.ExitCode != 0)
				throw new Exception(BuildCliFailureMessage("Workload Install", "dotnet " + string.Join(' ', args), r.GetOutput()));
		}

		async Task CliRepair(CancellationToken cancellationToken)
		{
			// dotnet workload install id --skip-manifest-update --add-source x
			var dotnetExe = Path.Combine(SdkRoot, DotNetSdk.DotNetExeName);

			var args = BuildRepairArgs(SdkVersion, NuGetPackageSources, Util.Verbose);

			ShellProcessRunner.ShellProcessResult r;

			if (!Util.IsWindows && !IsSdkPathWritable(SdkRoot))
			{
				Util.Log($"SDK path '{SdkRoot}' is not writable by the current user. Using elevated privileges.");
				r = await RetryWithSudo(dotnetExe, cancellationToken, args);
			}
			else
			{
				r = await Util.ShellCommand(dotnetExe, DotNetCliWorkingDir, Util.Verbose, cancellationToken, args);

				if (!Util.IsWindows && r.ExitCode != 0 && ShouldRetryWithSudo(r.GetOutput()))
				{
					r = await RetryWithSudo(dotnetExe, cancellationToken, args);
				}
			}

			if (cancellationToken.IsCancellationRequested)
				throw new OperationCanceledException(cancellationToken);

			// Throw if this failed with a bad exit code
			if (r.ExitCode != 0)
				throw new Exception(BuildCliFailureMessage("Workload Repair", "dotnet " + string.Join(' ', args), r.GetOutput()));
		}

		internal static string BuildCliFailureMessage(string operationName, string command, string output)
		{
			if (!string.IsNullOrWhiteSpace(output))
			{
				if (output.IndexOf("No space left on device", StringComparison.OrdinalIgnoreCase) >= 0
					|| output.IndexOf("There is not enough space on the disk", StringComparison.OrdinalIgnoreCase) >= 0
					|| output.IndexOf("disk full", StringComparison.OrdinalIgnoreCase) >= 0
					|| output.IndexOf("ENOSPC", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return $"{operationName} failed: Insufficient disk space detected. Free disk space (including temporary folders and package caches) and rerun `uno-check --fix`. Command: `{command}`";
				}

				var lines = output
					.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
					.Select(line => line.Trim())
					.Where(line => !string.IsNullOrWhiteSpace(line))
					.ToArray();

				if (lines.Length > 0)
				{
					string keyLine = null;

					for (var i = lines.Length - 1; i >= 0; i--)
					{
						var line = lines[i];
						if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
							|| line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
							|| line.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							keyLine = line;
							break;
						}
					}

					keyLine ??= lines[^1];
					keyLine = keyLine.TrimEnd('.');

					return $"{operationName} failed: {keyLine}. Command: `{command}`";
				}
			}

			return $"{operationName} failed: `{command}`";
		}

		/// <summary>
		/// Retries the command with sudo. Always tries <c>sudo -n</c> (non-interactive) first
		/// to leverage cached credentials or NOPASSWD rules without hanging. In interactive mode,
		/// falls back to prompting for the password in-process and piping it to <c>sudo -S</c>.
		/// </summary>
		async Task<ShellProcessRunner.ShellProcessResult> RetryWithSudo(string dotnetExe, CancellationToken cancellationToken, string[] args)
		{
			// Always try non-interactive sudo first (works with cached credentials or NOPASSWD)
			var result = await Util.WrapShellCommandWithSudoNoPrompt(dotnetExe, DotNetCliWorkingDir, Util.Verbose, cancellationToken, args);

			if (result.ExitCode == 0)
				return result;

			// In CI/non-interactive mode, sudo -n is the only option
			if (Util.NonInteractive || Util.CI)
				return result;

			// Interactive mode: prompt for password in-process and pipe to sudo -S
			Util.Log("Elevated privileges required. You may be prompted for your password.");
			return await Util.WrapShellCommandWithSudoInteractive(dotnetExe, DotNetCliWorkingDir, Util.Verbose, cancellationToken, args);
		}

		internal static bool ShouldRetryWithSudo(string output)
		{
			if (string.IsNullOrWhiteSpace(output))
			{
				return false;
			}

			return output.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) >= 0
				|| output.IndexOf("access to the path", StringComparison.OrdinalIgnoreCase) >= 0
				|| output.IndexOf("EACCES", StringComparison.OrdinalIgnoreCase) >= 0
				|| output.IndexOf("are required to perform this operation", StringComparison.OrdinalIgnoreCase) >= 0
				|| output.IndexOf("inadequate permissions", StringComparison.OrdinalIgnoreCase) >= 0
				|| output.IndexOf("elevated privileges", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>
		/// Checks whether the current user can write to the SDK directory.
		/// Used to proactively decide whether sudo is needed before attempting workload install,
		/// avoiding doomed user-level attempts that fail with download/restore errors
		/// rather than clear permission-denied messages.
		/// </summary>
		internal static bool IsSdkPathWritable(string sdkRoot)
		{
			try
			{
				var testPath = Path.Combine(sdkRoot, $".uno-check-write-test-{Guid.NewGuid():N}");
				using (File.Create(testPath, 1, FileOptions.DeleteOnClose)) { }
				return true;
			}
			catch
			{
				return false;
			}
		}

		private string FilterWorkloadCommandOutput(string output, (string begin, string end) marker)
		{
			var isNet8OrBelow = NuGetVersion.Parse(SdkVersion) < DotNetCheck.Manifest.DotNetSdk.Version9Preview3;

			if (isNet8OrBelow)
			{
				var startIndex = output.IndexOf(marker.begin);
				var endIndex = output.IndexOf(marker.end);

				if (startIndex >= 0 && endIndex >= 0)
				{
					// net8 and earlier use markers
					var start = startIndex + marker.begin.Length;
					output = output.Substring(start, endIndex - start);
				}
			}
			else
			{
				// This is needed to match the output of 
				// https://github.com/dotnet/sdk/blob/9a965db906ca70f57c4d44df1e0da09a5b662441/src/Cli/dotnet/commands/dotnet-workload/WorkloadIntegrityChecker.cs#L43
				var startIndex = output.IndexOf("{");

				if (startIndex >= 0)
				{
					output = output.Substring(startIndex);
				}
			}

			return output;
		}

	}
}
