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

		public NuGetVersion Version { get; set; }
	}

	public class OpenJdkCheckup : Models.Checkup
	{
		public NuGetVersion Version
			=> Extensions.ParseVersion(Manifest?.Check?.OpenJdk?.CompatVersion, new NuGetVersion("1.8.0-25"));

		public bool RequireExact
			=> Manifest?.Check?.OpenJdk?.RequireExact ?? false;

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

			var jdks = xamJdks.Concat(FindJdks())
				.GroupBy(j => j.Directory.FullName)
				.Select(g => g.First())
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

			if (Util.IsLinux)
			{
				return Task.FromResult(new DiagnosticResult(Status.Error, this,
					new Suggestion("Install OpenJDK11", "OpenJDK 11 is missing, follow the installation instructions here: https://learn.microsoft.com/en-us/java/openjdk/install#install-on-ubuntu")));
			}
			else
			{
				var url = Manifest?.Check?.OpenJdk?.Url;
				return Task.FromResult(new DiagnosticResult(Status.Error, this,
					new Suggestion("Install OpenJDK11",
						new BootsSolution(url, "Download and Install Microsoft OpenJDK 11"))));
			}
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
				var ms11Dir = Path.Combine("/Library", "Java", "JavaVirtualMachines", "microsoft-11.jdk", "Contents", "Home");
				SearchDirectoryForJdks(paths, ms11Dir, true);

				var msDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Developer", "Xamarin", "jdk");
				SearchDirectoryForJdks(paths, msDir, true);

				// /Library/Java/JavaVirtualMachines/
				try
				{
					var javaVmDir = Path.Combine("/Library", "Java", "JavaVirtualMachines");

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
						if (TryGetJavaJdkInfo(readlinkResult.StandardOutput[0], out var jdkInfo))
						{
							paths.Add(jdkInfo);
						}
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
					if (!found.Any(f => f.JavaC.FullName.Equals(file.FullName)) && TryGetJavaJdkInfo(file.FullName, out var jdkInfo))
						found.Add(jdkInfo);
				}
			}
		}

		static readonly Regex rxJavaCVersion = new Regex("[0-9\\.\\-_]+", RegexOptions.Singleline);

		bool TryGetJavaJdkInfo(string javacFilename, out OpenJdkInfo javaJdkInfo)
		{
			var r = ShellProcessRunner.Run(javacFilename, "-version");
			var m = rxJavaCVersion.Match(r.GetOutput() ?? string.Empty);

			var v = m?.Value;

			if (!string.IsNullOrEmpty(v) && NuGetVersion.TryParse(v, out var version))
			{
				javaJdkInfo = new OpenJdkInfo(javacFilename, version);
				return true;
			}

			javaJdkInfo = default;
			return false;
		}
	}
}
