using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotNetCheck.Models;
using DotNetCheck.Manifest;
using NuGet.Versioning;
using DotNetCheck.Solutions;
using Xamarin.Installer.AndroidSDK;
using Xamarin.Installer.AndroidSDK.Manager;
using System.IO;

namespace DotNetCheck.Checkups
{
	public class AndroidEmulatorCheckup : Checkup
	{
		private const string ArmArch = "arm64-v8a";
		private const string UnableToFindEmulatorsMessage = "Unable to find any Android Emulators.  See the Uno documentation for emulator setup: [underline]https://platform.uno/docs/articles/common-issues-mobile-debugging.html#android-emulator-setup[/]";
		public override IEnumerable<CheckupDependency> DeclareDependencies(IEnumerable<string> checkupIds)
			=> new [] { new CheckupDependency("androidsdk") };

		public IEnumerable<AndroidEmulator> RequiredEmulators
			=> Manifest?.Check?.Android?.Emulators;

		public override bool IsPlatformSupported(Platform platform)
			=> platform == Platform.OSX || platform == Platform.Windows || platform == Platform.Linux;
			
		public override string Id => "androidemulator";

		public override string Title => "Android Emulator";

		public override bool ShouldExamine(SharedState history)
			=> RequiredEmulators?.Any() ?? false;

		public override TargetPlatform GetApplicableTargets(Manifest.Manifest manifest) => TargetPlatform.Android;

		public override Task<DiagnosticResult> Examine(SharedState history)
		{
			if (history.GetEnvironmentVariable("ANDROID_EMULATOR_SKIP") == "true")
			{
				return Task.FromResult(
					new DiagnosticResult(Status.Warning, this, $"Installation skipped for https://github.com/unoplatform/uno.check/issues/48"));
			}

			AndroidSdk.AvdManager avdManager = null;

			var javaHome = history.GetEnvironmentVariable("LATEST_JAVA_HOME") ?? history.GetEnvironmentVariable("JAVA_HOME");
			string java = null;
			if (!string.IsNullOrEmpty(javaHome) && Directory.Exists(javaHome))
				java = Path.Combine(javaHome, "bin", "java" + (Util.IsWindows ? ".exe" : ""));

			var avds = new List<AndroidSdk.AvdManager.Avd>();

			// Try invoking the java avdmanager library first
			if (File.Exists(java))
			{
				avdManager = new AndroidSdk.AvdManager(java,
					history.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? history.GetEnvironmentVariable("ANDROID_HOME"));
				avds.AddRange(avdManager.ListAvds());
			}
			else
			{
				return Task.FromResult(
					new DiagnosticResult(Status.Error, this, $"Unable to find Java {java}"));
			}

			// Fallback to manually reading the avd files
			if (!avds.Any())
				avds.AddRange(AndroidSdk.AvdManager.ListAvdsFromFiles());

			if (avds.Any())
			{
				var emu = avds.FirstOrDefault();

				ReportStatus($"Emulator: {emu.Name ?? emu.SdkId} found.", Status.Ok);

				return Task.FromResult(DiagnosticResult.Ok(this));
			}

			// If we got here, there are no emulators at all
			var missingEmulators = RequiredEmulators;

			if (!missingEmulators.Any())
				return Task.FromResult(DiagnosticResult.Ok(this));

			AndroidSdk.AvdManager.AvdDevice preferredDevice = null;

			try
			{
				if (avdManager != null)
				{
					var devices = avdManager.ListDevices();

                    Util.Log($"Listing devices:");
					foreach (var device in devices)
					{
                        Util.Log($"Device: {device.Name} ({device.Id})");
					}

					preferredDevice = devices.FirstOrDefault(d => d.Name.Contains("pixel", StringComparison.OrdinalIgnoreCase));
				}
				else
				{
					ReportStatus($"Unable to find AVD manager", Status.Ok);
				}
			}
			catch (Exception ex)
			{
				ReportStatus(UnableToFindEmulatorsMessage, Status.Warning);

				Util.Exception(ex);
				return Task.FromResult(
					new DiagnosticResult(Status.Warning, this, msg));
			}

			return Task.FromResult(new DiagnosticResult(
				Status.Error,
				this,
				new Suggestion("Create an Android Emulator",
					missingEmulators.Select(me =>
						new ActionSolution((sln, cancel) =>
						{
							try
							{
								var installer = new AndroidSDKInstaller(new Helper(), AndroidManifestType.GoogleV2);

								var androidSdkPath = history.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? history.GetEnvironmentVariable("ANDROID_HOME");
								installer.Discover(new List<string> { androidSdkPath });

								var sdkInstance = installer.FindInstance(androidSdkPath);

								var installedPackages = sdkInstance.Components.AllInstalled(true);

								var sdkPackage = installedPackages.FirstOrDefault(p =>
								{
									// This will be false if the proccess runs on Rosetta emulation
									// and will install the wrong emulator (x86_64)
									// https://github.com/dotnet/runtime/issues/42130
									return Util.IsArm64

									// The Path will be something like:
									// system-images;android-33;google_apis;arm64-v8a (for arm)
									// system-images;android-31;google_apis;x86_64 (for x86 or x64)

									? p.Path.Contains(ArmArch, StringComparison.OrdinalIgnoreCase)
									: p.Path.Equals(me.SdkId, StringComparison.OrdinalIgnoreCase);
								});
								if (sdkPackage == null && (me.AlternateSdkIds?.Any() ?? false))
									sdkPackage = installedPackages.FirstOrDefault(p => me.AlternateSdkIds.Any(a => a.Equals(p.Path, StringComparison.OrdinalIgnoreCase)));

								var sdkId = sdkPackage?.Path ?? me.SdkId;

								var result = avdManager.Create(
									$"Android_Emulator_{me.ApiLevel}",
									sdkId,
									device: preferredDevice?.Id,
									tag: "google_apis",
									force: true,
									interactive: true);

								foreach (var msg in result.output)
								{
                                    Util.Log(msg);
								}

								if (result.success)
								{
									return Task.CompletedTask;
								}
								else
								{
									throw new Exception($"Unable to create Emulator");
								}
							}
							catch (Exception ex)
							{
								ReportStatus(UnableToFindEmulatorsMessage, Status.Warning);
								Util.Exception(ex);
							}

							return Task.CompletedTask;
						})).ToArray())));
		}
	}

}
