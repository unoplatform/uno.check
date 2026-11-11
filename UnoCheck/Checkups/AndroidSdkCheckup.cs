using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using DotNetCheck.Models;
using Polly;
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

		const int DownloadRetries = 3;

		// Bounds each network read rather than the whole download, as a system image is ~1.5 GB.
		static readonly TimeSpan DownloadStallTimeout = TimeSpan.FromMinutes(5);

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
				if (history.TryGetState<SkipInfo[]>(StateKey.EntryPoint, StateKey.Skips, out var skips))
				{
                    if (package.Path.StartsWith("system-images;")
						&& skips.Any(s => s.CheckupId == "androidemulator"))
                    {
                        ReportStatus($"{package.Path} ({package.Version}) skipped because the android emulator is skipped.", Status.Ok);
                        continue;
                    }
                }

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
						.OrderBy(p => p.Revision)
						.FirstOrDefault(p => p.Channel.ID == "channel-0"
							&& p.Path.Equals(packagePath, StringComparison.OrdinalIgnoreCase)
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
								await Task.WhenAll(downloads.Where(d => (d.HostArch == null) || (d.HostArch == (Util.IsArm64 ? "aarch64" : "x64"))).Select(d => Download(httpClient, d, cancelToken)));
							}

							if (ShouldInstallThroughStaging(Util.IsWindows, Util.IsDirectoryWritable(sdkInstance.Path)))
								await InstallThroughStagingAsync(installer, sdkInstance.Path, installationSet);
							else
								installer.Install(sdkInstance, installationSet);
						}
						catch (Exception ex)
						{
							// Swallowing here reported an applied fix for packages that were
							// never installed; the check that follows still failed. Log for the
							// verbose transcript, then let the fix runner see the failure.
							Util.Exception(ex);
							throw;
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
					},
					// Packages install into the discovered Android SDK: the usual per-user
					// location needs no elevation, a shared/Program Files one does.
					requiresElevation: !Util.IsDirectoryWritable(sdkInstance.Path)))));

		}

		/// <summary>
		/// The installer writes straight into the SDK, which macOS/Linux cannot elevate
		/// in-process; Windows elevates the whole fix child instead.
		/// </summary>
		internal static bool ShouldInstallThroughStaging(bool isWindows, bool sdkWritable)
			=> !isWindows && !sdkWritable;

		/// <summary>
		/// Installs into a user-writable staging folder laid out like the SDK, then copies it
		/// into <paramref name="sdkPath"/> with elevation (sudo or the authorization dialog).
		/// </summary>
		static async Task InstallThroughStagingAsync(AndroidSDKInstaller installer, string sdkPath, IList<IAndroidComponent> installationSet)
		{
			string staging = null;
			try
			{
				await Util.WrapCopyWithShellSudo(sdkPath, isFile: false, intermediate =>
				{
					staging = intermediate;
					Directory.CreateDirectory(staging);

					installer.Discover(new List<string> { staging }, fromScratch: false);
					var stagingInstance = installer.FindInstance(staging)
						?? throw new InvalidOperationException($"Could not prepare the Android SDK staging folder '{staging}'.");

					installer.Install(stagingInstance, installationSet);
					return Task.FromResult(true);
				});
			}
			finally
			{
				if (staging != null && Directory.Exists(staging))
				{
					try
					{
						Directory.Delete(staging, recursive: true);
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
					{
						Util.Log($"Could not delete the Android SDK staging folder '{staging}': {ex.Message}");
					}
				}
			}
		}

		IAndroidComponent FindInstalledPackage(IEnumerable<IAndroidComponent> installed, params Manifest.AndroidPackage[] acceptablePackages)
		{
			if (acceptablePackages?.Any() ?? false)
			{
				foreach (var p in acceptablePackages)
				{
					var v = !string.IsNullOrWhiteSpace(p.Version) ? new AndroidRevision(p.Version) : null;

					var installedPkg = installed
						.OrderBy(p => p.Revision)
						.FirstOrDefault(
						i => i.Path.Equals(p.Path.Trim(), StringComparison.OrdinalIgnoreCase)
							&& (i.Revision >= (v ?? i.Revision) || i.InstalledRevision >= (v ?? i.Revision)));

					if (installedPkg != null)
						return installedPkg;
				}
			}

			return default;
		}

		async Task Download(HttpClient httpClient, Archive archive, CancellationToken cancellationToken)
		{
			ReportStatus($"Downloading {archive.Url} ...", null);

			var path = Path.GetTempFileName();
			lock (temporaryFiles)
				temporaryFiles.Add(path);

			await DownloadArchiveAsync(httpClient, archive.Url, path, (long)archive.Size, DownloadRetries, DownloadStallTimeout,
				status => ReportStatus(status, null), Task.Delay, cancellationToken);

			ReportStatus($"Wrote '{archive.Url}' to '{path}'.", null);
			archive.DownloadedFilePath = path;
		}

		/// <summary>
		/// Downloads <paramref name="url"/> to <paramref name="path"/>. A dropped, stalled or short transfer is
		/// retried, resuming from the bytes already written when the server honors the Range request.
		/// </summary>
		internal static async Task DownloadArchiveAsync(HttpClient client, Uri url, string path, long expectedSize,
			int retries, TimeSpan stallTimeout, Action<string> reportStatus,
			Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
		{
			var fileName = Path.GetFileName(url.AbsolutePath);
			EntityTagHeaderValue etag = null;

			await Policy.Handle<HttpRequestException>().Or<TimeoutException>()
				.RetryAsync(retries, async (exception, attempt) =>
				{
					reportStatus($"Download of {fileName} interrupted ({exception.Message}); retrying ({attempt}/{retries}) ...");
					await delay(TimeSpan.FromSeconds(Math.Pow(2, Math.Min(attempt - 1, 4))), cancellationToken);
				})
				.ExecuteAsync(async token =>
				{
					using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
					timeout.CancelAfter(stallTimeout);
					try
					{
						var written = new FileInfo(path) is { Exists: true } file ? file.Length : 0;

						using var request = new HttpRequestMessage(HttpMethod.Get, url);
						if (written > 0)
						{
							request.Headers.Range = new RangeHeaderValue(written, null);
							// If the file changed on the server since, it answers 200 with all of it.
							if (etag != null)
								request.Headers.IfRange = new RangeConditionHeaderValue(etag);
						}

						using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
						var status = (int)response.StatusCode;
						var resumed = response.StatusCode == HttpStatusCode.PartialContent
							&& response.Content.Headers.ContentRange?.From == written;
						if (status == 416 || (response.StatusCode == HttpStatusCode.PartialContent && !resumed))
						{
							// The bytes written don't match the file on the server: start over.
							File.Delete(path);
							throw new HttpRequestException($"Downloading {fileName} could not resume (HTTP {status}).");
						}
						if (status == 408 || status == 429 || status >= 500)
							throw new HttpRequestException($"Downloading {fileName} returned HTTP {status}.");
						if (!response.IsSuccessStatusCode)
							throw new InvalidOperationException($"Downloading {fileName} returned HTTP {status} ({response.ReasonPhrase}).");

						if (!resumed)
						{
							written = 0;
							etag = response.Headers.ETag is { IsWeak: false } tag ? tag : null;
						}

						var total = response.Content.Headers.ContentRange?.Length
							?? written + response.Content.Headers.ContentLength
							?? (expectedSize > 0 ? expectedSize : null);

						using (var input = await response.Content.ReadAsStreamAsync())
						using (var output = new FileStream(path, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
						{
							var buffer = new byte[81920];
							var previousProgress = total > 0 ? (double)written / total.Value : 0;
							while (true)
							{
								timeout.CancelAfter(stallTimeout);
								int read;
								try
								{
									read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token);
								}
								catch (IOException ex)
								{
									throw new HttpRequestException($"Downloading {fileName} was interrupted after {written} bytes.", ex);
								}
								if (read == 0)
									break;
								await output.WriteAsync(buffer, 0, read, timeout.Token);
								written += read;

								// Log download progress roughly every 10%.
								var progress = total > 0 ? (double)written / total.Value : 0;
								if (progress - previousProgress > .10)
								{
									reportStatus($"Downloaded {progress:P0} of {fileName} ...");
									previousProgress = progress;
								}
							}
						}

						if (total.HasValue && written != total)
						{
							if (written > total)
								File.Delete(path);
							throw new HttpRequestException($"Downloaded {written} of {total} bytes of {fileName}.");
						}
					}
					catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
					{
						throw new TimeoutException($"Downloading {fileName} stalled.", ex);
					}
				}, cancellationToken);
		}
	}
}
