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
		
		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			string sdkRoot = default;

			if (sharedState != null && sharedState.TryGetEnvironmentVariable("DOTNET_ROOT", out var envSdkRoot))
			{
				// Check if DOTNET_ROOT points to a Homebrew location (which we shouldn't modify)
				if (Directory.Exists(envSdkRoot) && !envSdkRoot.Contains("/homebrew/", StringComparison.OrdinalIgnoreCase))
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
					? $"\"{scriptPath}\" -InstallDir \"{sdkRoot}\" -Version \"{Version}\""
					: $"\"{scriptPath}\" --install-dir \"{sdkRoot}\" --version \"{Version}\"";

			Util.Log($"Executing dotnet-install script...");
			Util.Log($"\t{exe} {args}");

			// Launch the process
			await Util.WrapShellCommandWithSudo(exe, [args]);

			// Update DOTNET_ROOT to point to where we installed, so subsequent checks use the correct location
			if (sharedState != null)
			{
				sharedState.SetEnvironmentVariable("DOTNET_ROOT", sdkRoot);
				Util.Log($"Updated DOTNET_ROOT to: {sdkRoot}");
			}
		}
	}
}
