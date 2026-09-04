using DotNetCheck;
using DotNetCheck.Models;
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
		const string installScriptBash = "https://dot.net/v1/dotnet-install.sh";
		const string installScriptPwsh = "https://dot.net/v1/dotnet-install.ps1";

		public DotNetSdkScriptInstallSolution(string version)
		{
			Version = version;
		}

		public readonly string Version;

		/// <summary>
		/// Depends on the machine layout: the default Windows root is under Program Files
		/// (elevation required), while a DOTNET_ROOT or the unix default of ~/.dotnet is
		/// user-writable. Probed rather than assumed, so a user-local SDK does not make a
		/// host prompt needlessly.
		/// </summary>
		public override bool RequiresElevation => !Util.IsDirectoryWritable(DefaultSdkRoot());

		/// <summary>
		/// The root <see cref="Implement"/> installs into when SharedState carries no
		/// DOTNET_ROOT. Kept in sync with the resolution there.
		/// </summary>
		internal static string DefaultSdkRoot()
		{
			var envRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
			if (!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot))
				return envRoot;

			return Util.IsWindows
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")
				: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
		}
		
		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			string sdkRoot = default;

			if (sharedState != null && sharedState.TryGetEnvironmentVariable("DOTNET_ROOT", out var envSdkRoot))
			{
				if (Directory.Exists(envSdkRoot))
					sdkRoot = envSdkRoot;
			}

			if (string.IsNullOrEmpty(sdkRoot))
				sdkRoot = Util.IsWindows
					? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")
					: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");

			var scriptUrl = Util.IsWindows ? installScriptPwsh : installScriptBash;
			var scriptPath = Path.Combine(Path.GetTempPath(), Util.IsWindows ? "dotnet-install.ps1" : "dotnet-install.sh");

			Util.Log($"Downloading dotnet-install script: {scriptUrl}");

			var http = new HttpClient();
			var data = await http.GetStringAsync(scriptUrl);
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
			// DOTNET_ROOT is elevated, and then only the install command itself.
			var result = !Util.IsWindows && IsDirectoryWritableOrCreatable(sdkRoot)
				? await Util.ShellCommand(exe, workingDir: null, verbose: Util.Verbose, cancellationToken: cancellationToken, args: args)
				: await Util.WrapShellCommandWithSudo(exe, workingDir: null, verbose: Util.Verbose, cancellationToken: cancellationToken, args: args);
			if (!result.Success)
				throw new InvalidOperationException(result.GetOutput());
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
