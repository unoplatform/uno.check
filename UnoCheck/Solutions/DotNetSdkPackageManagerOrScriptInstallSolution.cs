using DotNetCheck;
using DotNetCheck.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class DotNetSdkPackageManagerOrScriptInstallSolution : Solution
	{
		const string installScriptBash = "https://dot.net/v1/dotnet-install.sh";
		const string installScriptPwsh = "https://dot.net/v1/dotnet-install.ps1";
		const string environmentInstructionsUrl = "https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script#set-environment-variables";
		
		private readonly (LinuxPackageManagerWrapper, string)[] LinuxDotNet8PackageNames = new[]
		{
			(LinuxPackageManagerWrapper.Debian, "dotnet-sdk-8.0"), // may or may not be available on latest LTS as of 30/11/2023
			// (LinuxPackageManagerWrapper.Arch, "dotnet-sdk"), // Arch doesn't have a stable package name, uses dotnet-sdk for latest sdk and relies on aur for older releases
			(LinuxPackageManagerWrapper.FedoraRHEL, "dotnet-sdk-8.0"), // not available yet as of 30/11/2023, even Microsoft's own docs are misleading on this
			(LinuxPackageManagerWrapper.OldFedoraRHEL, "dotnet-sdk-8.0"), // will likely be unavailable, not tested
			// (LinuxPackageManagerWrapper.OpenSUSE, "dotnet-sdk-8.0") // not in the standard repos, must add Microsoft's repo
		};
		
		private readonly (LinuxPackageManagerWrapper, string)[] LinuxDotNet7PackageNames = new[]
		{
			(LinuxPackageManagerWrapper.Debian, "dotnet-sdk-7.0"),
			// (LinuxPackageManagerWrapper.Arch, "dotnet-sdk"), // Arch doesn't have a stable package name, uses dotnet-sdk for latest sdk and relies on aur for older releases
			(LinuxPackageManagerWrapper.FedoraRHEL, "dotnet-sdk-7.0"),
			(LinuxPackageManagerWrapper.OldFedoraRHEL, "dotnet-sdk-7.0"),
			// (LinuxPackageManagerWrapper.OpenSUSE, "dotnet-sdk-7.0") // not in the standard repos, must add Microsoft's repo
		};

		public DotNetSdkPackageManagerOrScriptInstallSolution(string version)
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
				if (Directory.Exists(envSdkRoot))
					sdkRoot = envSdkRoot;
			}
			
			Debug.Assert(!string.IsNullOrEmpty(sdkRoot));

			if (!Util.IsWindows && sdkRoot.StartsWith("/usr")) // installed using a package manager
			{
				// We first try to install the missing version(s) using the package manager too.
				// Failing that, we do NOT attempt to use the automated script as it will either fail or
				// cause conflicts with the existing managed installation.
				
				if (Util.Platform == Platform.Linux) // TODO: do the same for MacOS (perhaps using Homebrew)
				{
					var packageNamesWithWrappers = Version.Split(".")[0] switch
					{
						"7" => LinuxDotNet7PackageNames,
						"8" => LinuxDotNet8PackageNames,
						_ => Array.Empty<(LinuxPackageManagerWrapper, string)>()
					} ;

					if (packageNamesWithWrappers.Length > 0)
					{
						Debug.Assert(Util.Platform == Platform.Linux);
						var installedPackage = await LinuxPackageManagerWrapper.InstallPackage(packageNamesWithWrappers, true);

						if (installedPackage)
						{
							ReportStatus($"SUCCESS: a .NET {Version.Split(".")[0]} version was installed successfully using a package manager.");
					
							return;
						}
						else
						{
							ReportStatus($"FAIL: installing a .NET {Version.Split(".")[0]} version failed using a package manager.");
						}
					}
				}
				
				ReportStatus(
				$"""
				FAIL: installing missing .NET SDKs using uno-check will fail or interfere with the existing .NET installation if it was installed using a package manager.
				It's advised to install all SDK versions in the same way. Either uninstall the existing installation and reinstall using {installScriptBash} then rerun uno-check or
				install the missing version using your package manager.
				""");
				
				return;
			}
			
			if (!Util.IsWindows && !ShellProcessRunner.Run("command", $"-v bash").Success)
			{
				// the script specifically requires bash in the shebang and also has some bashisms
				ReportStatus($"FAIL: bash was not found. Installing using the script from {installScriptBash} requires bash.");
			}

			var scriptUrl = Util.IsWindows ? installScriptPwsh : installScriptBash;
			var scriptPath = Path.Combine(Path.GetTempPath(), Util.IsWindows ? "dotnet-install.ps1" : "dotnet-install.sh");

			Util.Log($"Downloading dotnet-install script: {scriptUrl}");

			var http = new HttpClient();
			var data = await http.GetStringAsync(scriptUrl);
			File.WriteAllText(scriptPath, data);

			var exe = Util.IsWindows ? "powershell" : "bash";

			var args = Util.IsWindows
					? $"\"{scriptPath}\" -InstallDir '{sdkRoot}' -Version {Version}"
					: $"\"{scriptPath}\" --install-dir '{sdkRoot}' --version {Version}";

			Util.Log($"Executing dotnet-install script...");
			Util.Log($"\t{exe} {args}");

			// Launch the process
			var p = new ShellProcessRunner(new ShellProcessRunnerOptions(exe, args));

			p.WaitForExit();

			ReportStatus($"WARNING: dotnet was installed from {scriptUrl}, but the script doesn't add the install location to the user's PATH environment variable, you must manually add it. To learn more, visit {environmentInstructionsUrl}");
		}
	}
}
