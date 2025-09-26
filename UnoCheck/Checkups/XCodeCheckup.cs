﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Claunia.PropertyList;
using DotNetCheck.Models;
using NuGet.Versioning;

namespace DotNetCheck.Checkups
{
	public class XCodeCheckup : Checkup
	{
		const string BugCommandLineToolsPath = "/Library/Developer/CommandLineTools";

		public override bool IsPlatformSupported(Platform platform)
			=> platform == Platform.OSX;

		public NuGetVersion MinimumVersion
			=> Extensions.ParseVersion(Manifest?.Check?.XCode?.MinimumVersion);

		public string MinimumVersionName
			=> Manifest?.Check?.XCode?.MinimumVersionName;

		public NuGetVersion ExactVersion
			=> Extensions.ParseVersion(Manifest?.Check?.XCode?.ExactVersion);

		public string ExactVersionName
			=> Manifest?.Check?.XCode?.ExactVersionName;

		public NuGetVersion Version
			=> ExactVersion ?? MinimumVersion;

		public string VersionName
			=> ExactVersionName ?? MinimumVersionName ?? ExactVersion?.ToString() ?? MinimumVersion?.ToString();

		public override string Id => "xcode";

		public override string Title => $"Required Xcode {VersionName} (newer version might not be supported)";

		public override bool ShouldExamine(SharedState history)
			=> Manifest?.Check?.XCode != null;

		private int install_tries;

		public override Task<DiagnosticResult> Examine(SharedState history)
		{
			try
			{
				var selected = GetSelectedXCode();

				if (selected is not null && selected.Version.IsCompatible(MinimumVersion, ExactVersion))
				{
					// customize runner options so the license can be displayed
					var options = new ShellProcessRunnerOptions("xcodebuild", "");
					var runner = new ShellProcessRunner(options);
					var result = runner.WaitForExit();
					// Check if user requires EULA to be accepted
					if (result.ExitCode == 69)
					{
						Spectre.Console.AnsiConsole.MarkupLine("[bold red]By fixing this you are accepting the license agreement.[/]");
						return Task.FromResult(new DiagnosticResult(
							Status.Error,
							this,
							new Suggestion("Run `sudo xcodebuild -license accept`",
								new Solutions.XcodeEulaSolution())));
					}

					var (isValid, sdkVersion) = ValidateForiOSSDK();

					if (isValid)
					{
						// Selected version is good
						ReportStatus($"Xcode.app ({selected.VersionString} {selected.BuildVersion})", Status.Ok);

						return Task.FromResult(DiagnosticResult.Ok(this));
					}

					ReportStatus($"Xcode.app ({selected.VersionString} {selected.BuildVersion}) is installed, but missing the iOS SDK ({sdkVersion}). Usually, this occurs after a recent Xcode install or update.", Status.Error);

					if (string.IsNullOrEmpty(sdkVersion))
					{
						// If we don't have a sdk version, it means xcrun failed, likely because the tools haven't been fully installed
						Spectre.Console.AnsiConsole.MarkupLine("Open Xcode to complete the installation of the iOS SDK");
						return Task.FromResult(new DiagnosticResult(
									Status.Error,
									this,
									new Suggestion("Run `open -a Xcode`",
										new Solutions.ActionSolution((sln, cancelToken) =>
										{
											ShellProcessRunner.Run("open", $"-a {selected.Path}");
											return Task.CompletedTask;
										}))));
					}

					// If we do have a sdk version, it means the tools are installed but the iOS SDK runtime is missing
					Spectre.Console.AnsiConsole.MarkupLine($"Installing the missing iOS SDK runtime version {sdkVersion}...");

					var tempPath = Path.Combine(Path.GetTempPath(), $"Uno.Check.iOS-{Guid.NewGuid()}");
					Directory.CreateDirectory(tempPath);

					return Task.FromResult(new DiagnosticResult(
						Status.Error,
						this,
						new Suggestion($"Run `xcodebuild -downloadPlatform iOS -exportPath {tempPath} -buildVersion {sdkVersion}`",
							new Solutions.ActionSolution((sln, cancelToken) =>
							{
								var result = ShellProcessRunner.Run("xcodebuild", $"-downloadPlatform iOS -exportPath {tempPath} -buildVersion {sdkVersion}");
								if (result.ExitCode != 0)
								{
									Spectre.Console.AnsiConsole.MarkupLine($"[bold red]Failed to download iOS SDK runtime. Exit code: {result.ExitCode}[/]");
								}
								return Task.CompletedTask;
							}))));
				}

				XCodeInfo eligibleXcode = null;

				var xcodes = FindXCodeInstalls();

				foreach (var x in xcodes)
				{
					if (x.Version.IsCompatible(MinimumVersion, ExactVersion))
					{
						eligibleXcode = x;
						break;
					}
				}

				if (eligibleXcode != null)
				{
					// If this is the case, they need to run xcode-select -s
					ReportStatus($"No Xcode.app or an incompatible Xcode.app version is selected, but one was found at ({eligibleXcode.Path})", Status.Error);

					return Task.FromResult(new DiagnosticResult(
						Status.Error,
						this,
						new Suggestion("Run xcode-select -s <Path>",
							new Solutions.ActionSolution((sln, cancelToken) =>
							{
								ShellProcessRunner.Run("xcode-select", "-s " + eligibleXcode.Path);
								return Task.CompletedTask;
							}))));
				}


				ReportStatus($"Xcode.app ({Version} {VersionName}) not installed.", Status.Error);

				return Task.FromResult(new DiagnosticResult(
					Status.Error,
					this,
					new Suggestion($"Download XCode {Version} {VersionName}")));
			}
			catch(InvalidDataException)
			{
				install_tries++;
				return Task.FromResult(new DiagnosticResult(
						Status.Error,
						this,
						install_tries > 1 ?
							new Suggestion($"Download XCode {VersionName}") :
							new Suggestion("Run xcode-select --install",
								new Solutions.ActionSolution((sln, cancelToken) =>
								{
									var result = ShellProcessRunner.Run("xcode-select", "--install");

									if(result.ExitCode == 0)
									{
										this.Examine(history);
									}

									return Task.CompletedTask;
								}))));
			}
		}

		XCodeInfo GetSelectedXCode()
		{
			var r = ShellProcessRunner.Run("xcode-select", "-p");

			var xcodeSelectedPath = r.GetOutput().Trim();

			if (!string.IsNullOrEmpty(xcodeSelectedPath))
			{
				if (xcodeSelectedPath.Equals(BugCommandLineToolsPath))
					throw new InvalidDataException();

				var infoPlist = Path.Combine(xcodeSelectedPath, "..", "Info.plist");
				if (File.Exists(infoPlist))
				{
					return GetXcodeInfo(
						Path.GetFullPath(
							Path.Combine(xcodeSelectedPath, "..", "..")), true);
				}
			}

			return null;
		}

		public static readonly string[] LikelyPaths = new []
		{
			"/Applications/Xcode.app",
			"/Applications/Xcode-beta.app",
		};

		IEnumerable<XCodeInfo> FindXCodeInstalls()
		{
			foreach (var p in LikelyPaths)
			{
				var i = GetXcodeInfo(p, false);
				if (i != null)
					yield return i;
			}
		}

		XCodeInfo GetXcodeInfo(string path, bool selected)
		{
			var versionPlist = Path.Combine(path, "Contents", "version.plist");

			if (File.Exists(versionPlist))
			{
				NSDictionary rootDict = (NSDictionary)PropertyListParser.Parse(versionPlist);
				string cfBundleShortVersion = rootDict.ObjectForKey("CFBundleShortVersionString")?.ToString();
				string productBuildVersion = rootDict.ObjectForKey("ProductBuildVersion")?.ToString();

				if (NuGetVersion.TryParse(cfBundleShortVersion, out var v))
					return new XCodeInfo(v, cfBundleShortVersion, productBuildVersion, path, selected);
			}
			else
			{
				var infoPlist = Path.Combine(path, "Contents", "Info.plist");

				if (File.Exists(infoPlist))
				{
					NSDictionary rootDict = (NSDictionary)PropertyListParser.Parse(infoPlist);
					string cfBundleVersion = rootDict.ObjectForKey("CFBundleVersion")?.ToString();
					string cfBundleShortVersion = rootDict.ObjectForKey("CFBundleShortVersionString")?.ToString();
					if (NuGetVersion.TryParse(cfBundleVersion, out var v))
						return new XCodeInfo(v, cfBundleShortVersion, string.Empty, path, selected);
				}
			}
			return null;
		}

		// Checking iOS SDK is a three-step process:
		// 1. Get the path to the iOS SDK using `xcrun -sdk iphonesimulator --show-sdk-path`
		// 2. Find the SDK Version in the SDKSettings.json file located at the SDK path
		// 3. Filter the iOS Runtime installed using the SDK Version
		static (bool isValid, string sdkVersion) ValidateForiOSSDK()
		{
			Util.Log($"Validating for iOS SDK...");
			try
			{
				var r = ShellProcessRunner.Run("xcrun", "-sdk iphonesimulator --show-sdk-path");
				var iphoneSimulatorSDKPath = r.GetOutput().Trim();

				if (Directory.Exists(iphoneSimulatorSDKPath))
				{					
					var sdkInfo = Path.Combine(iphoneSimulatorSDKPath, "SDKSettings.json");
					if (File.Exists(sdkInfo))
					{
						var text = File.ReadAllText(sdkInfo);
						var settings = System.Text.Json.JsonSerializer.Deserialize<SDKSettings>(text, new System.Text.Json.JsonSerializerOptions
						{
							PropertyNameCaseInsensitive = true
						});
						
						Util.Log($"Found iOS SDK at {iphoneSimulatorSDKPath}. Searching for iOS Runtime ({settings.Version})...");

						// Check if the SDK Runtime for iOS Simulator is installed
						var p = ShellProcessRunner.Run("xcrun", $"simctl list runtimes ios available | grep {settings.Version}");
						var runtimeOutput = p.GetOutput().Trim();

						Util.Log($"Found iOS Runtime: {runtimeOutput}");

						var isValid = !string.IsNullOrEmpty(runtimeOutput) && runtimeOutput.Contains(settings.Version);

						return (isValid, settings.Version);
					}
				}
			}
			catch (Exception ex)
			{
				Util.Exception(ex);
			}

			return (false, string.Empty);
		}
	}

	public record XCodeInfo(NuGetVersion Version, string VersionString, string BuildVersion, string Path, bool Selected);

	public record SDKSettings(string DisplayName, string Version);
}
