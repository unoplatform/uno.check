using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

		// The manifest's version name is the Xcode build (e.g. "15F31d"), taken from the same entry as the version.
		internal string RequiredVersion
			=> Targets.OlderOnlyXcodeVersion
				?? (ExactVersion is not null
					? FormatRequiredVersion(ExactVersion, ExactVersionName)
					: FormatRequiredVersion(MinimumVersion, MinimumVersionName));

		/// <summary>
		/// The <c>--tfm</c> Apple target frameworks, split by the Xcode they need. Resolved in <see cref="ShouldExamine"/>,
		/// which runs before the title is shown. Without Apple target frameworks, the manifest's Xcode is checked.
		/// </summary>
		internal XcodeTargets Targets { get; set; } = XcodeTargets.None;

		/// <summary>Whether <c>--tfm</c> was given at all. Without it, the project's .NET version is unknown.</summary>
		internal bool TargetFrameworksGiven { get; set; }

		/// <summary>The .NET version the manifest's SDK builds with its current packs: <c>net11.0</c> for SDK 11.0.100.</summary>
		internal string CurrentDotNetVersion
			=> (Manifest?.Check?.DotNet?.Sdks ?? [])
				.Select(sdk => NuGetVersion.TryParse(sdk?.Version, out var v) ? $"net{v.Major}.0" : null)
				.FirstOrDefault(v => v is not null);

		/// <summary>Whether an Xcode is the one to check: the older target frameworks' major.minor, or the manifest's.</summary>
		internal bool IsRequiredXcode(NuGetVersion version)
			=> Targets.OlderOnlyXcodeVersion is { } older
				? XcodeTargetFrameworks.MatchesMajorMinor(version, older)
				: version.IsCompatible(MinimumVersion, ExactVersion);

		public override string Id => "xcode";

		// The title keeps the plain version, so a build name never nests inside the caveat's parentheses.
		public override string Title
			=> Targets.OlderOnlyXcodeVersion is { } older
				? $"Required Xcode {older} for {string.Join(", ", Targets.Older.Select(o => o.TargetFramework))}"
				: $"Required Xcode {Version} (newer version might not be supported)";

		/// <summary>
		/// Why older Apple target frameworks need another Xcode, and how to use the manifest's Xcode instead:
		/// "net10.0-ios builds with this .NET SDK's net10.0 Apple SDK packs, which require Xcode 26.0. To use
		/// Xcode 27.2, move to net11.0-ios."
		/// </summary>
		internal static string FormatOlderTargetsReason(IReadOnlyList<OlderTarget> older, string currentDotNetVersion, string currentXcode)
		{
			var targetFrameworks = older.Select(o => o.TargetFramework).ToList();
			var message = $"{string.Join(", ", targetFrameworks)} {(targetFrameworks.Count == 1 ? "builds" : "build")} with this .NET SDK's "
				+ $"{older[0].DotNetVersion} Apple SDK packs, which require Xcode {older[0].XcodeVersion}.";

			if (currentDotNetVersion is null)
				return message;

			var retargeted = targetFrameworks.Select(t => XcodeTargetFrameworks.Retarget(t, currentDotNetVersion)).Distinct();

			return $"{message} To use Xcode {currentXcode}, move to {string.Join(", ", retargeted)}.";
		}

		/// <summary>
		/// The warning when the given target frameworks need different Xcode versions, so the checked Xcode can't
		/// build the older ones.
		/// </summary>
		internal static string FormatUncoveredTargetsWarning(IReadOnlyList<OlderTarget> uncovered, string selectedXcode, string currentDotNetVersion)
		{
			var needs = string.Join(", ", uncovered.Select(o => $"{o.TargetFramework} needs Xcode {o.XcodeVersion}"));
			var retargeted = currentDotNetVersion is null
				? null
				: string.Join(", ", uncovered.Select(o => XcodeTargetFrameworks.Retarget(o.TargetFramework, currentDotNetVersion)).Distinct());

			var (it, needsIt) = uncovered.Count == 1 ? ("it", "it needs") : ("them", "each needs");
			var select = $"select the Xcode {needsIt} when you build {it}.";

			return $"With this .NET SDK, {needs}, so the selected Xcode {selectedXcode} can't build {it}. "
				+ (retargeted is null ? char.ToUpperInvariant(select[0]) + select.Substring(1) : $"Move to {retargeted}, or {select}");
		}

		/// <summary>
		/// The hint shown without <c>--tfm</c> when the selected Xcode can't build an older target framework, because
		/// Uno templates still default to one (net10.0).
		/// </summary>
		internal static string FormatOlderTargetsHint(IReadOnlyDictionary<string, string> olderXcodeVersions, NuGetVersion selected, string selectedXcode, string currentDotNetVersion)
		{
			var unmatched = (olderXcodeVersions ?? new Dictionary<string, string>())
				.Where(e => XcodeTargetFrameworks.TryGetMajorMinor(e.Value, out var xcode) && !XcodeTargetFrameworks.MatchesMajorMinor(selected, xcode))
				.OrderBy(e => e.Key, StringComparer.Ordinal)
				.Select(e => $"{e.Key} Apple targets (iOS, tvOS, Mac Catalyst) need Xcode {e.Value}")
				.ToList();

			if (unmatched.Count == 0)
				return null;

			var builds = currentDotNetVersion is null ? string.Empty : $", and the selected Xcode {selectedXcode} builds {currentDotNetVersion} ones";

			return $"With this .NET SDK, {string.Join("; ", unmatched)}{builds}. Pass your app's target frameworks with --tfm to check them.";
		}

		internal const string DownloadUrl = "https://developer.apple.com/download/all/";

		// Suggestion names are rendered as Spectre markup, so the manifest-provided part is escaped.
		internal string DownloadSuggestion
			=> $"Download Xcode {Spectre.Console.Markup.Escape(RequiredVersion)} from {DownloadUrl}";

		internal static string FormatRequiredVersion(NuGetVersion version, string versionName)
		{
			if (version is null)
				return versionName ?? string.Empty;

			if (string.IsNullOrEmpty(versionName) || (NuGetVersion.TryParse(versionName, out var named) && named == version))
				return version.ToString();

			return $"{version} ({versionName})";
		}

		internal static string FormatMissingMessage(string requiredVersion, IEnumerable<string> installedVersions)
		{
			var installed = (installedVersions ?? Enumerable.Empty<string>())
				.Where(v => !string.IsNullOrWhiteSpace(v))
				.Distinct()
				.ToList();

			if (installed.Count == 0)
				return $"Xcode is not installed. Xcode {requiredVersion} is required.";

			return $"Xcode {string.Join(", ", installed)} {(installed.Count == 1 ? "is" : "are")} installed, but Xcode {requiredVersion} is required.";
		}

		// The message also goes on the result, so --json reports it instead of falling back to the suggestion.
		internal DiagnosticResult MissingXcodeResult(IEnumerable<XCodeInfo> installs)
		{
			var message = WithOlderTargetsReason(
				FormatMissingMessage(RequiredVersion, (installs ?? Enumerable.Empty<XCodeInfo>()).Select(FormatInstalledVersion)));

			ReportStatus(message, Status.Error);

			return new DiagnosticResult(Status.Error, this, message, new Suggestion(DownloadSuggestion));
		}

		internal static string FormatInstalledVersion(XCodeInfo x)
		{
			// The Info.plist fallback can leave the short version empty; the parsed version or the path still names it.
			var version = string.IsNullOrEmpty(x.VersionString) ? x.Version?.ToString() : x.VersionString;

			if (string.IsNullOrEmpty(version))
				return x.Path;

			return string.IsNullOrEmpty(x.BuildVersion) ? version : $"{version} ({x.BuildVersion})";
		}

		// Appends why older target frameworks need this Xcode, when they are what's checked, ending the message first.
		internal string WithOlderTargetsReason(string message)
		{
			if (Targets.OlderOnlyXcodeVersion is null)
				return message;

			var sentence = message.TrimEnd();
			if (!sentence.EndsWith(".", StringComparison.Ordinal))
				sentence += ".";

			return $"{sentence} {FormatOlderTargetsReason(Targets.Older, CurrentDotNetVersion, FormatRequiredVersion(Version, null))}";
		}

		public override bool ShouldExamine(SharedState history)
		{
			string[] targetFrameworks = null;
			history?.TryGetState(StateKey.EntryPoint, StateKey.TargetFrameworks, out targetFrameworks);

			TargetFrameworksGiven = targetFrameworks is { Length: > 0 };
			Targets = XcodeTargetFrameworks.Classify(targetFrameworks, Manifest?.Check?.XCode?.TargetFrameworks);

			return Manifest?.Check?.XCode != null;
		}

		private int install_tries;

		public override Task<DiagnosticResult> Examine(SharedState history)
		{
			try
			{
				var selected = GetSelectedXCode();

				if (selected is not null && IsRequiredXcode(selected.Version))
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

						if (Targets.Uncovered.Count > 0)
						{
							var warning = FormatUncoveredTargetsWarning(Targets.Uncovered, selected.VersionString, CurrentDotNetVersion);
							ReportStatus(warning, Status.Warning);

							return Task.FromResult(new DiagnosticResult(Status.Warning, this, warning));
						}

						if (!TargetFrameworksGiven
							&& FormatOlderTargetsHint(Manifest?.Check?.XCode?.TargetFrameworks, selected.Version, selected.VersionString, CurrentDotNetVersion) is { } hint)
						{
							ReportStatus(hint, null);
						}

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
					Spectre.Console.AnsiConsole.MarkupLine($"Installing the missing iOS SDK runtime version {Spectre.Console.Markup.Escape(sdkVersion)}...");

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
									Spectre.Console.AnsiConsole.MarkupLine($"[bold red]Failed to download iOS SDK runtime. Exit code: {Spectre.Console.Markup.Escape(result.ExitCode.ToString())}[/]");
								}
								return Task.CompletedTask;
							}))));
				}

				XCodeInfo eligibleXcode = null;

				var xcodes = FindXCodeInstalls().ToList();

				foreach (var x in xcodes)
				{
					if (IsRequiredXcode(x.Version))
					{
						eligibleXcode = x;
						break;
					}
				}

				if (eligibleXcode != null)
				{
					// If this is the case, they need to run xcode-select -s
					ReportStatus(
						WithOlderTargetsReason($"No Xcode.app or an incompatible Xcode.app version is selected, but one was found at ({eligibleXcode.Path})"),
						Status.Error);

					return Task.FromResult(new DiagnosticResult(
						Status.Error,
						this,
						new Suggestion("Run xcode-select -s <Path>",
							new Solutions.ActionSolution(async (sln, cancelToken) =>
							{
								var result = await Util.WrapShellCommandWithSudo(
									"/usr/bin/xcode-select",
									workingDir: null,
									verbose: Util.Verbose,
									cancellationToken: cancelToken,
									args: new[] { "-s", eligibleXcode.Path });
								if (!result.Success)
									throw new InvalidOperationException(result.GetOutput());
							}))));
				}


				// The selected Xcode may live outside /Applications, so it's listed too.
				return Task.FromResult(MissingXcodeResult(selected is null ? xcodes : xcodes.Append(selected)));
			}
			catch(InvalidDataException)
			{
				install_tries++;
				return Task.FromResult(new DiagnosticResult(
						Status.Error,
						this,
						install_tries > 1 ?
							new Suggestion(DownloadSuggestion) :
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
			foreach (var p in OrderXcodePaths(FindXcodeApps()))
			{
				var i = GetXcodeInfo(p, false);
				if (i != null)
					yield return i;
			}
		}

		// Side-by-side installs are usually renamed (Xcode_26.5.app, or Xcode-26.5.0.app from Xcodes.app).
		static IEnumerable<string> FindXcodeApps()
		{
			try
			{
				return Directory.EnumerateDirectories("/Applications", "Xcode*.app").ToList();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				Util.Exception(ex);
				return Enumerable.Empty<string>();
			}
		}

		// The usual locations come first, so the same Xcode is picked as before when it's compatible.
		internal static IEnumerable<string> OrderXcodePaths(IEnumerable<string> found)
			=> LikelyPaths.Concat((found ?? Enumerable.Empty<string>())
				.Where(p => !LikelyPaths.Contains(p, StringComparer.Ordinal))
				.OrderBy(p => p, StringComparer.Ordinal));

		// A corrupt or half-extracted Xcode is skipped rather than failing the whole check.
		internal static NSDictionary TryReadPlist(string file)
		{
			try
			{
				return PropertyListParser.Parse(file) as NSDictionary;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PropertyListFormatException
				or FormatException or System.Xml.XmlException or ArgumentException or IndexOutOfRangeException)
			{
				Util.Log($"Could not read {file}: {ex.Message}");
				return null;
			}
		}

		XCodeInfo GetXcodeInfo(string path, bool selected)
		{
			var versionPlist = Path.Combine(path, "Contents", "version.plist");

			if (File.Exists(versionPlist))
			{
				NSDictionary rootDict = TryReadPlist(versionPlist);
				string cfBundleShortVersion = rootDict?.ObjectForKey("CFBundleShortVersionString")?.ToString();
				string productBuildVersion = rootDict?.ObjectForKey("ProductBuildVersion")?.ToString();

				if (NuGetVersion.TryParse(cfBundleShortVersion, out var v))
					return new XCodeInfo(v, cfBundleShortVersion, productBuildVersion, path, selected);
			}
			else
			{
				var infoPlist = Path.Combine(path, "Contents", "Info.plist");

				if (File.Exists(infoPlist))
				{
					NSDictionary rootDict = TryReadPlist(infoPlist);
					string cfBundleVersion = rootDict?.ObjectForKey("CFBundleVersion")?.ToString();
					string cfBundleShortVersion = rootDict?.ObjectForKey("CFBundleShortVersionString")?.ToString();
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
