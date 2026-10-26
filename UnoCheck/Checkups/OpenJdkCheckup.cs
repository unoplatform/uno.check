using DotNetCheck.Models;
using DotNetCheck.Solutions;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xamarin.Android.Tools;

namespace DotNetCheck.Checkups
{
	public class OpenJdkInfo
	{
		public OpenJdkInfo(string javaCFile, NuGetVersion version)
		{
			JavaC = new FileInfo(javaCFile);
			Version = version;
		}

		public FileInfo JavaC { get; set; }

		public DirectoryInfo Directory
			=> new DirectoryInfo(Path.Combine(JavaC.Directory.FullName, ".."));

		// Null when `javac -version` didn't give a version we could read.
		public NuGetVersion Version { get; set; }
	}

	public class OpenJdkCheckup : Models.Checkup
	{
		public NuGetVersion Version
			=> Extensions.ParseVersion(Manifest?.Check?.OpenJdk?.CompatVersion, new NuGetVersion("1.8.0-25"));

		public bool RequireExact
			=> Manifest?.Check?.OpenJdk?.RequireExact ?? false;

		internal string Requirement
			=> RequireExact ? $"OpenJDK {Version}" : $"OpenJDK {Version} or later";

		const string LinuxInstallUrl = "https://learn.microsoft.com/en-us/java/openjdk/install#install-on-ubuntu";

		public string MajorVersion
		{
			get
			{
				var versionString = Manifest?.Check?.OpenJdk?.Version;
				if (!string.IsNullOrEmpty(versionString) && System.Version.TryParse(versionString, out var version))
				{
					return version.Major.ToString();
				}
				return "17";
			}
		}

		public override string Id => "openjdk";

		public override string Title => $"OpenJDK {Version}";

		static string PlatformJavaCExtension => Util.IsWindows ? ".exe" : string.Empty;

		public override bool IsPlatformSupported(Platform platform)
			=> platform == Platform.OSX || platform == Platform.Windows || platform == Platform.Linux;

		public override bool ShouldExamine(SharedState history)
			=> Manifest?.Check?.OpenJdk != null;

		public override TargetPlatform GetApplicableTargets(Manifest.Manifest manifest) => TargetPlatform.Android;

		public override Task<DiagnosticResult> Examine(SharedState history)
		{
			var xamJdks = new List<OpenJdkInfo>();
			try
			{
				var xamSdkInfo = new AndroidSdkInfo((traceLevel, msg) => Util.Log(msg), null, null, null);

				if (!string.IsNullOrEmpty(xamSdkInfo.JavaSdkPath))
					SearchDirectoryForJdks(xamJdks, xamSdkInfo.JavaSdkPath);
			}
			catch (Exception ex)
			{
				Util.Exception(ex);
			}

			var found = xamJdks.Concat(FindJdks())
				.GroupBy(j => j.Directory.FullName)
				.Select(g => g.First())
				.ToList();

			foreach (var unreadable in found.Where(j => j.Version is null))
				ReportStatus($"{unreadable.JavaC.FullName} (version not recognized)", null);

			var jdks = found
				.Where(j => j.Version is not null)
				.OrderBy(s => s.Version);

			var ok = false;

			foreach (var jdk in jdks)
			{
				if ((jdk.JavaC.FullName.Contains("microsoft", StringComparison.OrdinalIgnoreCase) || jdk.JavaC.FullName.Contains("openjdk", StringComparison.OrdinalIgnoreCase))
					&& jdk.Version.IsCompatible(Version, RequireExact ? Version : null))
				{
					ok = true;
					ReportStatus($"{jdk.Version} ({jdk.Directory})", Status.Ok);
					history.SetEnvironmentVariable("JAVA_HOME", jdk.Directory.FullName);

					// Try and set the global env var on windows if it's not set
					if (Util.IsWindows && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JAVA_HOME")))
					{
						try
						{
							Environment.SetEnvironmentVariable("JAVA_HOME", jdk.Directory.FullName, EnvironmentVariableTarget.Machine);
							ReportStatus($"Set Environment Variable: JAVA_HOME={jdk.Directory.FullName}", Status.Ok);
						}
						catch { }
					}
				}
				else
					ReportStatus($"{jdk.Version} ({jdk.Directory.FullName})", null);
			}

            // Setup the latest LATEST_JAVA_HOME
            if (jdks.Any())
            {
                var latest = jdks.Last();
                history.SetEnvironmentVariable("LATEST_JAVA_HOME", latest.Directory.FullName);
            }

            if (ok)
				return Task.FromResult(DiagnosticResult.Ok(this));

			return Task.FromResult(NoCompatibleJdkResult(found));
		}

		// OpenJDK is only reported missing when no javac was found at all. Otherwise the message names what was found,
		// and goes on the result too, so --json reports it instead of falling back to the suggestion.
		internal DiagnosticResult NoCompatibleJdkResult(IEnumerable<OpenJdkInfo> found)
		{
			var message = FormatIncompatibleMessage(Requirement, found);

			if (message is not null)
				ReportStatus(message, Status.Error);

			if (Util.IsLinux)
			{
				var description = message is null
					? $"OpenJDK {MajorVersion} is missing, follow the installation instructions here: {LinuxInstallUrl}"
					: $"Follow the installation instructions here: {LinuxInstallUrl}";

				return new DiagnosticResult(Status.Error, this, message,
					new Suggestion($"Install OpenJDK{MajorVersion}", description));
			}
			else
			{
				var url = Manifest?.Check?.OpenJdk?.Url;
				return new DiagnosticResult(Status.Error, this, message,
					new Suggestion($"Install OpenJDK{MajorVersion}",
						new BootsSolution(url, $"Download and Install Microsoft OpenJDK {MajorVersion}")));
			}
		}

		internal static string FormatIncompatibleMessage(string requirement, IEnumerable<OpenJdkInfo> found)
		{
			var names = (found ?? Enumerable.Empty<OpenJdkInfo>())
				.Select(j => j.Version is null
					? $"{j.JavaC.FullName} (version not recognized)"
					: $"{j.Version} ({j.Directory.FullName})")
				.ToList();

			if (names.Count == 0)
				return null;

			return $"{requirement} is required, but none of the JDKs found is compatible: {string.Join(", ", names)}.";
		}

		IEnumerable<OpenJdkInfo> FindJdks()
		{
			var paths = new List<OpenJdkInfo>();

			if (Util.IsWindows)
			{
				SearchDirectoryForJdks(paths,
					Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android", "Jdk"), true);

				var pfmsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft");

				try
				{
					if (Directory.Exists(pfmsDir))
					{
						var msJdkDirs = Directory.EnumerateDirectories(pfmsDir, "jdk-*", SearchOption.TopDirectoryOnly);
						foreach (var msJdkDir in msJdkDirs)
							SearchDirectoryForJdks(paths, msJdkDir, false);
					}
				}
				catch (Exception ex)
				{
					Util.Exception(ex);
				}

				SearchDirectoryForJdks(paths,
					Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Jdk"), true);
			}
			else if (Util.IsMac)
			{
				var msJdkDir = Path.Combine("/Library", "Java", "JavaVirtualMachines", $"microsoft-{MajorVersion}.jdk", "Contents", "Home");
				SearchDirectoryForJdks(paths, msJdkDir, true);

				var msDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Developer", "Xamarin", "jdk");
				SearchDirectoryForJdks(paths, msDir, true);

				// /Library/Java/JavaVirtualMachines/
				try
				{
					var javaVmDir = Path.Combine("Library", "Java", "JavaVirtualMachines");

					if (Directory.Exists(javaVmDir))
					{
						var javaVmJdkDirs = Directory.EnumerateDirectories(javaVmDir, "*.jdk", SearchOption.TopDirectoryOnly);
						foreach (var javaVmJdkDir in javaVmJdkDirs)
							SearchDirectoryForJdks(paths, javaVmDir, true);

						javaVmJdkDirs = Directory.EnumerateDirectories(javaVmDir, "jdk-*", SearchOption.TopDirectoryOnly);
						foreach (var javaVmJdkDir in javaVmJdkDirs)
							SearchDirectoryForJdks(paths, javaVmDir, true);
					}
				}
				catch (Exception ex)
				{
					Util.Exception(ex);
				}
			}

			SearchDirectoryForJdks(paths, Environment.GetEnvironmentVariable("JAVA_HOME") ?? string.Empty, true);
			SearchDirectoryForJdks(paths, Environment.GetEnvironmentVariable("JDK_HOME") ?? string.Empty, true);

			var environmentPaths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();

			foreach (var envPath in environmentPaths)
			{
				if (envPath.Contains("java", StringComparison.OrdinalIgnoreCase) || envPath.Contains("jdk", StringComparison.OrdinalIgnoreCase))
					SearchDirectoryForJdks(paths, envPath, true);
			}

			if (Util.IsLinux)
			{
				var r = ShellProcessRunner.Run("whereis", "-b javac");

				if (
					r.Success
					&& r.StandardOutput.Count > 0
					&& r.StandardOutput[0].Split(" ").Skip(1).FirstOrDefault() is { } javacBinPath)
				{
					var readlinkResult = ShellProcessRunner.Run("readlink", "-f " + javacBinPath);

					if (
						readlinkResult.Success
						&& r.StandardOutput.Count > 0)
					{
						paths.Add(GetJavaJdkInfo(readlinkResult.StandardOutput[0]));
					}
				}
			}

			return paths
				.GroupBy(i => i.JavaC.FullName)
				.Select(g => g.First());
		}

		void SearchDirectoryForJdks(IList<OpenJdkInfo> found, string directory, bool recursive = true)
		{
			if (string.IsNullOrEmpty(directory))
				return;

			var dir = new DirectoryInfo(directory);

			if (dir.Exists)
			{
				var files = dir.EnumerateFileSystemInfos($"javac{PlatformJavaCExtension}", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

				foreach (var file in files)
				{
					if (!found.Any(f => f.JavaC.FullName.Equals(file.FullName)))
						found.Add(GetJavaJdkInfo(file.FullName));
				}
			}
		}

		static readonly Regex rxJavaCVersion = new Regex("[0-9\\.\\-_]+", RegexOptions.Singleline);

		// Captures the first four numeric parts of a longer version, e.g. 17.0.21.0 in 17.0.21.0.2.
		static readonly Regex rxExtraVersionParts = new Regex("^([0-9]+(?:\\.[0-9]+){3})(?:\\.[0-9]+)+");

		// A javac whose version can't be read is kept, with no version, so it's reported rather than dropped.
		static OpenJdkInfo GetJavaJdkInfo(string javacFilename)
		{
			var r = ShellProcessRunner.Run(javacFilename, "-version");

			TryParseJavaCVersion(r.GetOutput(), out var version);

			return new OpenJdkInfo(javacFilename, version);
		}

		internal static bool TryParseJavaCVersion(string javacOutput, out NuGetVersion version)
		{
			var v = rxJavaCVersion.Match(javacOutput ?? string.Empty).Value;

			// NuGetVersion takes at most four numeric parts, but Microsoft OpenJDK builds can report five (17.0.21.0.2).
			// The first four are enough to compare against the manifest, and the full string is kept for display.
			var trimmed = rxExtraVersionParts.Replace(v, "$1");

			if (!string.IsNullOrEmpty(trimmed) && NuGetVersion.TryParse(trimmed, out var parsed))
			{
				version = trimmed == v
					? parsed
					: new NuGetVersion(parsed.Version, parsed.ReleaseLabels, parsed.Metadata, v);
				return true;
			}

			version = null;
			return false;
		}
	}
}
