using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetCheck.Models;
using Xamarin.Android.Tools;
using Xamarin.Installer.AndroidSDK;
using Xamarin.Installer.AndroidSDK.Common;
using Xamarin.Installer.AndroidSDK.Manager;

namespace DotNetCheck.Checkups
{
	public class AndroidSdkPackagesCheckup : Models.Checkup
	{
		public override IEnumerable<CheckupDependency> DeclareDependencies(IEnumerable<string> checkupIds)
			=> new [] { new CheckupDependency("openjdk") };

		public IEnumerable<Manifest.AndroidPackage> RequiredPackages
			=> Manifest?.Check?.Android?.Packages;

		public override string Id => "androidsdk";

		public override string Title => "Android SDK";

		List<string> temporaryFiles = new List<string>();

		public override bool IsPlatformSupported(Platform platform)
			=> platform == Platform.OSX || platform == Platform.Windows || platform == Platform.Linux;

		public override bool ShouldExamine(SharedState history)
			=> RequiredPackages?.Any() ?? false;

		public override TargetPlatform GetApplicableTargets(Manifest.Manifest manifest) => TargetPlatform.Android;

		string[] macSdkLocations = new string[]
		{
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Android", "sdk"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Android", "android-sdk-macosx"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Developer", "Xamarin", "Android", "sdk"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Developer", "Xamarin", "android-sdk-macosx"),
			Environment.GetEnvironmentVariable("ANDROID_HOME")
		};

		string[] unixSdkLocations = new string[] 
		{
			Environment.GetEnvironmentVariable("ANDROID_HOME")
		};

		string[] winSdkLocations = new string[]
		{
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Android", "android-sdk"),
			Environment.GetEnvironmentVariable("ANDROID_HOME")
		};

		public string FindBestSdkLocation()
        {
			var possibleLocations = Util.IsWindows ? winSdkLocations : (Util.IsMac ? macSdkLocations : unixSdkLocations);

			foreach (var p in possibleLocations)
            {
				if (Directory.Exists(p) && (Directory.GetFileSystemEntries(p)?.Any() ?? false))
					return p;
            }

			return DefaultSdkLocation;
        }

		public string DefaultSdkLocation
			=> Util.IsWindows
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Android", "android-sdk")
				: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Android", "sdk");

		class AndroidComponentWrapper
		{
			public IAndroidComponent Component
			{
				get;set;
			}

			public override string ToString()
			{
				return Component.Path + " - " + Component.FileSystemPath;
			}
		}

		public override Task<DiagnosticResult> Examine(SharedState history)
		{
			var jdkPath = history.GetEnvironmentVariable("JAVA_HOME") ?? Environment.GetEnvironmentVariable("JAVA_HOME");

			string androidSdkPath = null;

			try
			{
				// Set the logger to override the default one that is set in this library
				// So we can catch output from failed path lookups that are otherwise swallowed
				var _ = new AndroidSdkInfo((traceLevel, msg) =>
				{

					if (Util.Verbose || traceLevel == System.Diagnostics.TraceLevel.Error)
						Util.LogAlways(msg);

				}, androidSdkPath, null, jdkPath);
			}
			catch (Exception ex)
			{
				Util.Exception(ex);
			}

			if (string.IsNullOrEmpty(androidSdkPath))
				androidSdkPath = FindBestSdkLocation();

			var missingPackages = new List<IAndroidComponent>();

			var installer = new AndroidSDKInstaller(new Helper(), AndroidManifestType.GoogleV2);

			installer.Discover(new List<string> { androidSdkPath });

			var sdkInstance = installer.FindInstance(androidSdkPath);

			if (string.IsNullOrEmpty(sdkInstance?.Path))
			{
				try
				{
					var data = new System.Net.WebClient().DownloadData("https://dl.google.com/android/repository/sys-img/android-desktop/sys-img2-3.xml");
				}
				catch(System.Net.WebException ex)
                {
					if(ex.Response is System.Net.HttpWebResponse response && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
						history.SetEnvironmentVariable("ANDROID_EMULATOR_SKIP", "true");

						return Task.FromResult(
						new DiagnosticResult(
							Status.Warning,
							this,
							"Failed to find Android SDK (See https://github.com/unoplatform/uno.check/issues/48 for more information).",
							new Suggestion("Failed to validate the Android SDK (See [underline]https://github.com/unoplatform/uno.check/issues/48[/] for details).",
							"For more information to install the Android SDK see: [underline]https://aka.ms/dotnet-androidsdk-help[/]")));
					}
				}

				return Task.FromResult(
				new DiagnosticResult(
					Status.Error,
					this,
					"Failed to find Android SDK.",
					new Suggestion("Install the Android SDK",
					"For more information see: [underline]https://aka.ms/dotnet-androidsdk-help[/]")));
			}

			history.SetEnvironmentVariable("ANDROID_SDK_ROOT", sdkInstance.Path);
			history.SetEnvironmentVariable("ANDROID_HOME", sdkInstance.Path);

			var installed = sdkInstance?.Components?.AllInstalled(true);

			foreach (var package in RequiredPackages)
			{
				var v = !string.IsNullOrWhiteSpace(package.Version) ? new AndroidRevision(package.Version) : null;

				var installedPkg = FindInstalledPackage(installed, package)
					?? FindInstalledPackage(installed, package.Alternatives?.ToArray());

				if (installedPkg == null)
				{
					var packagePath = package.Path.Trim();
					var packageVersion = package.Version;

					if(packagePath.EndsWith("x86_64") && Util.IsArm64)
					{
						// On arm64 devices, use arm64 emulators
						if(package.Alternatives.FirstOrDefault(a => a.Path.EndsWith("arm64-v8a")) is { } alternative)
						{
							packagePath = alternative.Path;
							packageVersion = alternative.Version;
						}
					}

					var pkgToInstall = sdkInstance?.Components?.AllNotInstalled()?
						.FirstOrDefault(p => p.Path.Equals(packagePath, StringComparison.OrdinalIgnoreCase)
							&& p.Revision >= (v ?? p.Revision));

					ReportStatus($"{packagePath} ({packageVersion}) missing.", Status.Error);

					if (pkgToInstall != null)
						missingPackages.Add(pkgToInstall);
				}
				else
				{
					if (!package.Path.Equals(installedPkg.Path) || v != (installedPkg.Revision ?? installedPkg.InstalledRevision))
						ReportStatus($"{installedPkg.Path} ({installedPkg.InstalledRevision ?? installedPkg.Revision})", Status.Ok);
					else
						ReportStatus($"{package.Path} ({package.Version})", Status.Ok);
				}
			}

			if (!missingPackages.Any())
				return Task.FromResult(DiagnosticResult.Ok(this));


			var installationSet = installer.GetInstallationSet(sdkInstance, missingPackages);

			var desc =
@$"Your Android SDK has missing or outdated packages.
You can use the Android SDK Manager to install / update them.
For more information see: [underline]https://aka.ms/dotnet-androidsdk-help[/]";

			return Task.FromResult(new DiagnosticResult(
				Status.Error,
				this,
				new Suggestion("Install or Update Android SDK packages",
					desc,
					new Solutions.ActionSolution(async (sln, cancelToken) =>
					{
						try
						{
							var downloads = installer.GetDownloadItems(installationSet);
							using (var httpClient = new HttpClient())
							{
									// Provide a default timeout value of 7 minutes if a value is not provided.
								httpClient.Timeout = TimeSpan.FromMinutes(120);
								await Task.WhenAll(downloads.Where(d => (d.HostArch == null) || (d.HostArch == (Util.IsArm64 ? "aarch64" : "x64"))).Select(d => Download(httpClient, d)));
							}

							installer.Install(sdkInstance, installationSet);
						}
						catch (Exception ex)
						{
							Util.Exception(ex);
						}
						finally
						{
							foreach (var temp in temporaryFiles)
							{
								if (File.Exists(temp))
								{
									try
									{
										File.Delete(temp);
									}
									catch { }
								}
							}
						}
					}))));

		}

		IAndroidComponent FindInstalledPackage(IEnumerable<IAndroidComponent> installed, params Manifest.AndroidPackage[] acceptablePackages)
		{
			if (acceptablePackages?.Any() ?? false)
			{
				foreach (var p in acceptablePackages)
				{
					var v = !string.IsNullOrWhiteSpace(p.Version) ? new AndroidRevision(p.Version) : null;

					var installedPkg = installed.FirstOrDefault(
						i => i.Path.Equals(p.Path.Trim(), StringComparison.OrdinalIgnoreCase)
							&& (i.Revision >= (v ?? i.Revision) || i.InstalledRevision >= (v ?? i.Revision)));

					if (installedPkg != null)
						return installedPkg;
				}
			}

			return default;
		}

		async Task Download(HttpClient httpClient, Archive archive)
		{
			ReportStatus($"Downloading {archive.Url} ...", null);

			using (var response = await httpClient.GetAsync(archive.Url, HttpCompletionOption.ResponseHeadersRead))
			{
				response.EnsureSuccessStatusCode();
				var fileLength = response.Content.Headers.ContentLength.Value;
				var path = Path.GetTempFileName();
				temporaryFiles.Add(path);
				using (var fileStream = File.OpenWrite(path))
				{
					using (var httpStream = await response.Content.ReadAsStreamAsync())
					{
						var buffer = new byte[16 * 1024];
						int bytesRead;
						double bytesWritten = 0;
						double previousProgress = 0;
						while ((bytesRead = httpStream.Read(buffer, 0, buffer.Length)) > 0)
						{
							fileStream.Write(buffer, 0, bytesRead);
							bytesWritten += bytesRead;
							// Log download progress roughly every 10%.
							var progress = bytesWritten / fileLength;
							if (progress - previousProgress > .10)
							{
								ReportStatus($"Downloaded {progress:P0} of {Path.GetFileName(archive.Url.AbsolutePath)} ...", null);
								previousProgress = progress;
							}
						}
						fileStream.Flush();
					}
				}
				ReportStatus($"Wrote '{archive.Url}' to '{path}'.", null);
				archive.DownloadedFilePath = path;
			}
		}
	}
}
