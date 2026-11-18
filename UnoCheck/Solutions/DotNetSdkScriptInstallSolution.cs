using DotNetCheck;
using DotNetCheck.Models;
using Polly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class DotNetSdkScriptInstallSolution : Solution
	{
		// dot.net only redirects to builds.dotnet.microsoft.com, so the script is downloaded from there first, and
		// dot.net is the fallback in case the script moves. On 2026-11-12, dot.net stalled or dropped connections
		// from GitHub's runners for hours, while builds.dotnet.microsoft.com kept working (issue #627).
		static readonly Uri[] InstallScriptBashUrls =
		{
			new Uri("https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh"),
			new Uri("https://dot.net/v1/dotnet-install.sh"),
		};

		static readonly Uri[] InstallScriptPwshUrls =
		{
			new Uri("https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.ps1"),
			new Uri("https://dot.net/v1/dotnet-install.ps1"),
		};

		const int ScriptDownloadRetries = 3;

		// The script is about 60 KB and usually arrives in a second. A slower attempt is abandoned and
		// retried rather than waiting for HttpClient's 100-second default, which then failed the whole run.
		static readonly TimeSpan ScriptDownloadAttemptTimeout = TimeSpan.FromSeconds(30);

		// Each attempt is bounded by ScriptDownloadAttemptTimeout instead.
		static readonly HttpClient ScriptClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

		public DotNetSdkScriptInstallSolution(string version)
			: this(version, null)
		{
		}

		public DotNetSdkScriptInstallSolution(string version, SharedState state)
		{
			Version = version;
			InstallRoot = ResolveInstallRoot(state);
		}

		public readonly string Version;

		/// <summary>
		/// The single root this solution both probes for writability and installs into.
		/// Resolved once, at construction, so the elevation answer a host receives cannot
		/// describe a different installation than the one the fix will write to.
		/// </summary>
		internal string InstallRoot { get; }

		/// <summary>
		/// Depends on the machine layout: the default Windows root is under Program Files
		/// (elevation required), while a DOTNET_ROOT or the unix default of ~/.dotnet is
		/// user-writable. Probed rather than assumed, so a user-local SDK does not make a
		/// host prompt needlessly.
		/// </summary>
		public override bool RequiresElevation => !Util.IsDirectoryWritable(InstallRoot);

		/// <summary>
		/// Resolves the root the install script writes to. The run's shared state carries the
		/// explicitly requested SDK root and therefore outranks the process environment: a
		/// writable DOTNET_ROOT in the environment must not make an install into a protected
		/// requested root look as though it needs no elevation.
		/// </summary>
		internal static string ResolveInstallRoot(SharedState state)
		{
			if (state != null
				&& state.TryGetEnvironmentVariable("DOTNET_ROOT", out var stateRoot)
				&& !string.IsNullOrEmpty(stateRoot)
				&& Directory.Exists(stateRoot))
			{
				return stateRoot;
			}

			var envRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
			if (!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot))
				return envRoot;

			return DefaultSdkRoot();
		}

		/// <summary>
		/// The root used when neither the run nor the environment names one.
		/// </summary>
		internal static string DefaultSdkRoot()
		{
			if (Util.IsWindows)
			{
				var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
				if (string.IsNullOrEmpty(programFiles))
					programFiles = Environment.GetEnvironmentVariable("ProgramFiles");

				return AppendFolder(programFiles, "dotnet", @"C:\Program Files\dotnet");
			}

			var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (string.IsNullOrEmpty(home))
				home = Environment.GetEnvironmentVariable("HOME");

			return AppendFolder(home, ".dotnet", "/usr/local/share/dotnet");
		}

		/// <summary>
		/// Joins <paramref name="folder"/> under <paramref name="root"/>. Path.Combine returns
		/// the child on its own when the root is empty — GetFolderPath yields an empty string
		/// whenever the folder is unavailable — and discards the root entirely when the child is
		/// rooted. Either would send the install to an unintended location, so both cases fall
		/// back to <paramref name="fallback"/> rather than silently producing a different path.
		/// </summary>
		static string AppendFolder(string root, string folder, string fallback)
			=> string.IsNullOrEmpty(root) || Path.IsPathRooted(folder)
				? fallback
				: Path.Combine(root, folder);

		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			var sdkRoot = InstallRoot;

			var scriptUrls = Util.IsWindows ? InstallScriptPwshUrls : InstallScriptBashUrls;
			var scriptPath = Path.Combine(Path.GetTempPath(), Util.IsWindows ? "dotnet-install.ps1" : "dotnet-install.sh");

			Util.Log($"Downloading dotnet-install script: {scriptUrls[0]}");

			var data = await DownloadScriptAsync(ScriptClient, scriptUrls, ScriptDownloadRetries, ScriptDownloadAttemptTimeout,
				ReportStatus, Task.Delay, cancellationToken);
			File.WriteAllText(scriptPath, data);

			var exe = Util.Platform switch
			{
				Platform.Linux => "bash",
				Platform.OSX => "bash",
				Platform.Windows => "powershell",
				_ => throw new NotSupportedException($"Unsupported platform {Util.Platform}")
			};

			var args = Util.IsWindows
				? new[] { Util.QuoteForProcessArgs(scriptPath), "-InstallDir", Util.QuoteForProcessArgs(sdkRoot), "-Version", Version }
				: new[] { scriptPath, "--install-dir", sdkRoot, "--version", Version };

			Util.Log($"Executing dotnet-install script...");
			Util.Log($"\t{exe} {string.Join(" ", args)}");

			// A user-local SDK install stays in the user's context. Only a protected
			// root is elevated, and then only the install command itself.
			var result = !Util.IsWindows && IsDirectoryWritableOrCreatable(sdkRoot)
				? await Util.ShellCommand(exe, workingDir: null, verbose: Util.Verbose, cancellationToken: cancellationToken, args: args)
				: await Util.WrapShellCommandWithSudo(exe, workingDir: null, verbose: Util.Verbose, cancellationToken: cancellationToken, args: args);
			if (!result.Success)
				throw new InvalidOperationException(result.GetOutput());
		}

		/// <summary>
		/// Downloads the dotnet-install script, trying <paramref name="urls"/> in turn. A failed, stalled or
		/// unsuccessful attempt is retried from the next URL, with backoff as in
		/// <see cref="Checkups.AndroidSdkPackagesCheckup.DownloadArchiveAsync"/>. When the retries are used up, a stalled
		/// download fails with a <see cref="TimeoutException"/>, so it isn't taken for the user cancelling the run.
		/// </summary>
		internal static async Task<string> DownloadScriptAsync(HttpClient client, IReadOnlyList<Uri> urls, int retries, TimeSpan attemptTimeout,
			Action<string> reportStatus, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
		{
			var tries = 0;

			return await Policy.Handle<HttpRequestException>().Or<TimeoutException>()
				.RetryAsync(retries, async (exception, attempt) =>
				{
					reportStatus($"Downloading the dotnet-install script failed ({exception.Message}); retrying from {urls[attempt % urls.Count].Host} ({attempt}/{retries}) ...");
					await delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
				})
				.ExecuteAsync(async token =>
				{
					var url = urls[tries++ % urls.Count];
					using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
					timeout.CancelAfter(attemptTimeout);
					try
					{
						using var response = await client.GetAsync(url, timeout.Token);
						// Any status is retried: a missing script on one host can still be on the other.
						if (!response.IsSuccessStatusCode)
							throw new HttpRequestException($"Downloading {url} returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

						// GetAsync has already read the whole body within the attempt's timeout. (netcoreapp3.1 has no
						// ReadAsStringAsync overload that takes a token.)
						return await response.Content.ReadAsStringAsync();
					}
					catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
					{
						throw new TimeoutException($"Downloading {url} took longer than {attemptTimeout.TotalSeconds:0} seconds.", ex);
					}
				}, cancellationToken);
		}

		/// <summary>
		/// Kept as the name this solution's callers and tests already use; the probe itself
		/// lives in <see cref="Util.IsDirectoryWritable"/> so the elevation decision here and
		/// the <see cref="RequiresElevation"/> answer reported to hosts can never diverge.
		/// </summary>
		internal static bool IsDirectoryWritableOrCreatable(string path)
			=> Util.IsDirectoryWritable(path);
	}
}
