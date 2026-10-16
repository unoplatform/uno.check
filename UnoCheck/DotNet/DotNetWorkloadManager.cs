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
			IsUserLocalInstall = IsUserLocalWorkloadInstall(sdkRoot, sdkVersion);

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

		/// <summary>
		/// Whether this SDK installs workloads per user instead of into <see cref="SdkRoot"/>.
		/// See <see cref="IsUserLocalWorkloadInstall"/>.
		/// </summary>
		public readonly bool IsUserLocalInstall;

		/// <summary>
		/// Whether installing workloads needs elevation: only when they go into a
		/// <see cref="SdkRoot"/> the current user cannot write to.
		/// </summary>
		public bool RequiresElevation => RequiresElevationFor(IsUserLocalInstall, IsSdkPathWritable(SdkRoot));

		internal static bool RequiresElevationFor(bool isUserLocalInstall, bool isSdkRootWritable)
			=> !isUserLocalInstall && !isSdkRootWritable;

		/// <summary>
		/// Whether running a workload command under <c>sudo</c> reaches the workloads the
		/// current user sees. Never on Windows, which has no sudo. Never for a user-local
		/// install either: the CLI then writes to the invoking user's home, which under sudo is
		/// root's (<c>/root/.dotnet</c>), so the install "succeeds" where the user's builds and
		/// <c>dotnet workload list</c> never look (issue #515).
		/// </summary>
		internal static bool CanUseSudoFor(bool isWindows, bool isUserLocalInstall)
			=> !isWindows && !isUserLocalInstall;

		bool CanUseSudo => CanUseSudoFor(Util.IsWindows, IsUserLocalInstall);

		/// <summary>
		/// Whether the SDK at <paramref name="sdkRoot"/> installs workloads per user for the
		/// feature band of <paramref name="sdkVersion"/>. A distribution opts in by shipping an
		/// empty <c>metadata/workloads/&lt;band&gt;/userlocal</c> marker under the .NET root, as
		/// Ubuntu's apt packages do for <c>/usr/lib/dotnet</c>. The CLI then installs workloads
		/// into <c>$DOTNET_CLI_HOME/.dotnet</c> (or <c>$HOME/.dotnet</c>), so the root never needs
		/// to be writable. The band in the marker path never carries a preview label: 10.0.100-rc.1
		/// and 10.0.112 both use <c>10.0.100</c>. See
		/// https://github.com/dotnet/sdk/blob/main/documentation/general/workloads/user-local-workloads.md
		/// </summary>
		/// <remarks>
		/// The band comes from the requested SDK version. The CLI runs from a global.json with
		/// <c>rollForward: latestFeature</c>, so it could resolve a higher band in the same root;
		/// distribution packages ship a single band per root, which this covers.
		/// </remarks>
		internal static bool IsUserLocalWorkloadInstall(string sdkRoot, string sdkVersion)
		{
			if (string.IsNullOrEmpty(sdkRoot) || !NuGetVersion.TryParse(sdkVersion, out var version))
				return false;

			var featureBand = $"{version.Major}.{version.Minor}.{version.Patch / 100 * 100}";
			return File.Exists(Path.Combine(sdkRoot, "metadata", "workloads", featureBand, "userlocal"));
		}

		/// <summary>
		/// Environment overlay applied to every <c>dotnet workload</c> invocation in this class so
		/// the CLI emits English output regardless of the host's UI culture. The matchers in
		/// <see cref="ShouldRetryWithSudo"/> and <see cref="BuildCliFailureMessage"/> look for
		/// English substrings ("permission denied", "no space left on device", etc.); without
		/// this override, those checks silently miss on, e.g., fr-FR or ja-JP hosts where dotnet
		/// localizes its diagnostics.
		///
		/// Applied two ways: (1) the static ctor copies the entries into
		/// <see cref="Util.EnvironmentVariables"/>, which <c>ShellProcessRunner</c> auto-injects
		/// into every child process — handles the non-sudo path. (2) The sudo call sites in
		/// <see cref="RetryWithSudo"/> and <see cref="GetInstalledWorkloads"/> invoke through
		/// <c>env</c> so the pin survives sudo's default <c>env_reset</c>, which would otherwise
		/// strip it before launching <c>dotnet</c>.
		///
		/// Mirrors <c>DotNetNewUnoTemplatesCheckup.GetDotNetNewInstalledList</c> which already
		/// pins the same variable for the same reason.
		/// </summary>
		internal static readonly IReadOnlyDictionary<string, string> WorkloadCliEnv = new Dictionary<string, string>
		{
			["DOTNET_CLI_UI_LANGUAGE"] = "en-US",
		};

		static DotNetWorkloadManager()
		{
			foreach (var kv in WorkloadCliEnv)
				Util.EnvironmentVariables[kv.Key] = kv.Value;
		}

		/// <summary>
		/// Builds an args list for invoking <paramref name="dotnetExe"/> through <c>env</c>, with
		/// <see cref="WorkloadCliEnv"/> re-set inside the elevated child. Required for sudo paths
		/// because sudo's default <c>env_reset</c> strips any var we set on the parent process.
		///
		/// Two variants exist because the interactive sudo consumer still accepts a rendered
		/// argument string. Shell and graphical consumers accept a typed argument list and
		/// apply their own escaping.
		/// </summary>
		static string[] BuildEnglishCliSudoArgsForShell(string dotnetExe, string[] args)
		{
			var result = new List<string>(args.Length + WorkloadCliEnv.Count + 1);
			foreach (var kv in WorkloadCliEnv)
				result.Add($"{kv.Key}={kv.Value}");
			result.Add(dotnetExe);
			result.AddRange(args);
			return result.ToArray();
		}

		/// <summary>
		/// Companion to <see cref="BuildEnglishCliSudoArgsForShell"/> for the non-shell sudo path
		/// (<see cref="Util.WrapShellCommandWithSudoInteractive"/>, <c>UseSystemShell=false</c>).
		/// That helper joins args with spaces and lets .NET re-tokenize the resulting string with
		/// Windows-style argv rules — there is no shell, so the <c>\$</c>/<c>\`</c>/<c>\\</c>
		/// escapes <see cref="Util.ShellDoubleQuote"/> inserts would survive as literal characters
		/// and corrupt the dotnet path. <see cref="Util.QuoteForProcessArgs"/> produces quoting that
		/// the argv parser will round-trip back to the original path (and still wraps space-bearing
		/// paths in <c>"…"</c>).
		/// </summary>
		static string[] BuildEnglishCliSudoArgsForProcess(string dotnetExe, string[] args)
		{
			var result = new List<string>(args.Length + WorkloadCliEnv.Count + 1);
			foreach (var kv in WorkloadCliEnv)
				result.Add($"{kv.Key}={kv.Value}");
			result.Add(Util.QuoteForProcessArgs(dotnetExe));
			result.AddRange(args.Select(Util.QuoteForProcessArgs));
			return result.ToArray();
		}

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
		///
		/// Returns true if the install can proceed (Windows, user-local or writable SDK,
		/// non-interactive run, or sudo credentials successfully cached). Returns false only when the
		/// interactive <c>sudo -v</c> handshake itself failed (wrong password, sudo
		/// unavailable, policy denial); callers must NOT enter the live spinner in that
		/// case, because the in-spinner sudo retry would re-prompt for the password from
		/// underneath the spinner and reproduce issue #515.
		/// </summary>
		public Task<bool> PrepareForInstallAsync(CancellationToken cancellationToken = default)
		{
			if (Util.IsWindows)
				return Task.FromResult(true);

			if (!RequiresElevation)
				return Task.FromResult(true);

			if (Util.NonInteractive || Util.CI)
				return Task.FromResult(true);

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
				rollbackFile
			};
			args.AddRange(workloadIds);
			foreach (var packageSource in packageSources)
			{
				args.Add(addSourceArg);
				args.Add(packageSource);
			}

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
			foreach (var packageSource in packageSources)
			{
				args.Add(addSourceArg);
				args.Add(packageSource);
			}

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

			string[] userInstalled = null;
			var userContextSucceeded = r.ExitCode == 0;

			if (userContextSucceeded)
			{
				var output = FilterWorkloadCommandOutput(string.Join(" ", r.StandardOutput), ListOutputMarker);
				userInstalled = ParseInstalledWorkloadIds(output);
			}

			// On Linux/macOS, workload installs may be split between the current user and a previous
			// elevated install, and the user-context call may also fail outright with "Inadequate
			// permissions" when the SDK is root-owned. Probe with `sudo -n` (no prompt, fails fast
			// without cached credentials) and union the results so mixed/elevated installs aren't
			// reported as missing. A user-local SDK skips the probe: under sudo it lists root's
			// workloads, which the current user cannot use.
			string[] sudoInstalled = null;
			var sudoContextAttempted = CanUseSudo;
			var sudoContextSucceeded = false;
			if (sudoContextAttempted)
			{
				// Invoke through `env` so DOTNET_CLI_UI_LANGUAGE survives sudo's default env_reset
				// — see WorkloadCliEnv for the full rationale.
				var sudoResult = await Util.WrapShellCommandWithSudoNoPrompt("env", DotNetCliWorkingDir, Util.Verbose, BuildEnglishCliSudoArgsForShell(dotnetExe, args.ToArray()));
				if (sudoResult.ExitCode == 0)
				{
					sudoContextSucceeded = true;
					var sudoOutput = FilterWorkloadCommandOutput(string.Join(" ", sudoResult.StandardOutput), ListOutputMarker);
					sudoInstalled = ParseInstalledWorkloadIds(sudoOutput);
				}
			}

			return CombineInstalledWorkloads(
				userContextSucceeded,
				userInstalled,
				sudoContextAttempted,
				sudoContextSucceeded,
				sudoInstalled,
				"dotnet " + string.Join(' ', args));
		}

		// example of output {"installed":["wasm-tools"],"updateAvailable":[]}
		internal static string[] ParseInstalledWorkloadIds(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return [];

			try
			{
				var workloads = JsonSerializer.Deserialize<WorkloadListResult>(json);
				return workloads?.installed ?? [];
			}
			catch (JsonException ex)
			{
				// Don't swallow: an unparseable response from `dotnet workload list
				// --machine-readable` would otherwise look identical to "no workloads
				// installed", producing false missing-workload reports and triggering
				// unnecessary repair/install attempts. Surface the format mismatch so
				// the caller fails loudly instead.
				var snippet = json.Length > 500 ? json[..500] + "…" : json;
				throw new InvalidDataException(
					$"Could not parse `dotnet workload list --machine-readable` output. Output: {snippet}",
					ex);
			}
		}

		/// <summary>
		/// Merges the user-context and sudo-context workload lists into a single deduplicated set.
		/// Throws only when both probes failed — a successful sudo probe alone is sufficient to
		/// report the elevated workload state on a permission-mismatched setup.
		/// </summary>
		internal static string[] CombineInstalledWorkloads(
			bool userContextSucceeded,
			string[] userInstalled,
			bool sudoContextAttempted,
			bool sudoContextSucceeded,
			string[] sudoInstalled,
			string failingCommand)
		{
			if (!userContextSucceeded && !(sudoContextAttempted && sudoContextSucceeded))
				throw new Exception($"Workload command failed: `{failingCommand}`");

			var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (userContextSucceeded && userInstalled != null)
			{
				foreach (var id in userInstalled)
					installed.Add(id);
			}
			if (sudoContextAttempted && sudoContextSucceeded && sudoInstalled != null)
			{
				foreach (var id in sudoInstalled)
					installed.Add(id);
			}
			return installed.ToArray();
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

			var r = await RunWorkloadCommand(dotnetExe, args, cancellationToken);

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

			var r = await RunWorkloadCommand(dotnetExe, args, cancellationToken);

			if (cancellationToken.IsCancellationRequested)
				throw new OperationCanceledException(cancellationToken);

			// Throw if this failed with a bad exit code
			if (r.ExitCode != 0)
				throw new Exception(BuildCliFailureMessage("Workload Repair", "dotnet " + string.Join(' ', args), r.GetOutput()));
		}

		/// <summary>
		/// Runs a workload command that writes workloads, elevating on Linux/macOS when the
		/// workloads go into an SDK root the current user cannot write to.
		/// </summary>
		async Task<ShellProcessRunner.ShellProcessResult> RunWorkloadCommand(string dotnetExe, string[] args, CancellationToken cancellationToken)
		{
			if (IsUserLocalInstall)
				Util.Log($"SDK '{SdkRoot}' installs workloads per user; running as the current user without elevation.");

			// Check writability before the user-level attempt: when the root isn't writable, use
			// sudo directly to avoid doomed user-level attempts that fail with download/restore
			// errors instead of clear permission-denied messages.
			if (CanUseSudo && RequiresElevation)
			{
				Util.Log($"SDK path '{SdkRoot}' is not writable by the current user. Using elevated privileges.");
				return await RetryWithSudo(dotnetExe, cancellationToken, args);
			}

			var r = await Util.ShellCommand(dotnetExe, DotNetCliWorkingDir, Util.Verbose, cancellationToken, args);

			if (CanUseSudo && r.ExitCode != 0 && ShouldRetryWithSudo(r.GetOutput()))
			{
				r = await RetryWithSudo(dotnetExe, cancellationToken, args);
			}

			return r;
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
		Task<ShellProcessRunner.ShellProcessResult> RetryWithSudo(string dotnetExe, CancellationToken cancellationToken, string[] args)
			=> RunWithSudoAsync(dotnetExe, DotNetCliWorkingDir, cancellationToken, args);

		/// <summary>
		/// Runs a dotnet CLI command elevated, with the English CLI environment: the system
		/// authorization dialog on structured macOS/Linux hosts, otherwise <c>sudo -n</c> first
		/// and, in interactive mode, a password prompt piped to <c>sudo -S</c>.
		/// </summary>
		internal static async Task<ShellProcessRunner.ShellProcessResult> RunWithSudoAsync(string dotnetExe, string workingDir, CancellationToken cancellationToken, string[] args)
		{
			// Structured macOS/Linux hosts opt into the system authorization dialog. Do not
			// use an unrelated terminal sudo ticket: authorization belongs to this fix request.
			// Deliberate trade: a user who just authenticated in a terminal still gets the
			// dialog, and each protected command is its own authorization — a workload
			// repair + install can prompt twice for one fix (recorded in spec 003's host flow).
			if (Util.UseAdministratorPrompt)
			{
				return await Util.WrapShellCommandWithSudo(
					"env",
					workingDir,
					Util.Verbose,
					cancellationToken,
					BuildEnglishCliSudoArgsForShell(dotnetExe, args));
			}

			// Always try non-interactive sudo first (works with cached credentials or NOPASSWD).
			// Invoke through `env` so DOTNET_CLI_UI_LANGUAGE survives sudo's default env_reset —
			// see WorkloadCliEnv for the full rationale. The two sudo helpers tokenize args
			// differently (sh -c '…' vs Windows-style argv), so the dotnet path has to be quoted
			// for the right consumer — see the two builders for why a single shared array breaks
			// when the path contains $/backtick/quote/backslash.
			var shellSudoArgs = BuildEnglishCliSudoArgsForShell(dotnetExe, args);
			var result = await Util.WrapShellCommandWithSudoNoPrompt("env", workingDir, Util.Verbose, cancellationToken, shellSudoArgs);

			if (result.ExitCode == 0)
				return result;

			// In CI/non-interactive mode, sudo -n is the only option
			if (Util.NonInteractive || Util.CI)
				return result;

			// Interactive mode: prompt for password in-process and pipe to sudo -S
			Util.Log("Elevated privileges required. You may be prompted for your password.");
			var processSudoArgs = BuildEnglishCliSudoArgsForProcess(dotnetExe, args);
			return await Util.WrapShellCommandWithSudoInteractive("env", workingDir, Util.Verbose, cancellationToken, processSudoArgs);
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
