using DotNetCheck.DotNet;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck
{
	public enum ManifestChannel
	{
		Default,
        Preview,
        PreviewMajor,
        Main
    }

	public class ToolInfo
	{
		public const string ToolName = "Uno Platform Check";
		public const string ToolPackageId = "Uno.Check";
		public const string ToolCommand = "uno-check";

		public static async Task<Manifest.Manifest> LoadManifest(string fileOrUrl, ManifestChannel channel)
		{
			var f = fileOrUrl ??
				channel switch
				{
                    ManifestChannel.Preview => Manifest.Manifest.PreviewManifestUrl,
                    ManifestChannel.PreviewMajor => Manifest.Manifest.PreviewMajorManifestUrl,
                    ManifestChannel.Main => Manifest.Manifest.DefaultManifestUrl,
					ManifestChannel.Default => Manifest.Manifest.DefaultManifestUrl,
					_ => Manifest.Manifest.DefaultManifestUrl
				};

			Util.Log($"Loading Manifest from: {f}");

			return await Manifest.Manifest.FromFileOrUrl(f);
		}

		public static NuGetVersion CurrentVersion
			=> NuGetVersion.Parse(FileVersionInfo.GetVersionInfo(typeof(ToolInfo).Assembly.Location).FileVersion);

		public static bool Validate(Manifest.Manifest manifest)
		{
			var toolVersion = manifest?.Check?.ToolVersion ?? "0.1.0";

			Util.Log($"Required Version: {toolVersion}");

			var fileVersion = NuGetVersion.Parse(FileVersionInfo.GetVersionInfo(typeof(ToolInfo).Assembly.Location).FileVersion);

			Util.Log($"Current Version: {fileVersion}");

			if (string.IsNullOrEmpty(toolVersion) || !NuGetVersion.TryParse(toolVersion, out var toolVer) || fileVersion < toolVer)
			{
				Console.WriteLine();
				AnsiConsole.MarkupLine($"[bold red]{Icon.Error} Updating to version {toolVersion} or newer is required:[/]");
				AnsiConsole.MarkupLine($"[blue]  dotnet tool update --global {ToolPackageId}[/]");

				if (Debugger.IsAttached)
				{
					if (AnsiConsole.Confirm("Mismatched version, continue debugging anyway?"))
						return true;
				}

				return false;
			}

			var minSupportedDotNetSdkVersion = Manifest.DotNetSdk.Version6Preview7;

			// Check that we aren't on a manifest that wants too old of dotnet6
			if (manifest?.Check?.DotNet?.Sdks?.Any(dnsdk =>
				NuGetVersion.TryParse(dnsdk.Version, out var dnsdkVersion) && dnsdkVersion < minSupportedDotNetSdkVersion) ?? false)
			{
				Console.WriteLine();
				AnsiConsole.MarkupLine($"[bold red]{Icon.Error} This version of the tool is incompatible with installing an older version of .NET 6[/]");
				return false;
			}

			return true;
		}

		public static Task<NuGetVersion> GetLatestVersion(bool includePrerelease) => 
			NuGetHelper.GetLatestPackageVersionAsync("Uno.Check", includePrerelease);

        public static void ExitPrompt(bool nonInteractive)
		{
			if (!nonInteractive)
			{
				AnsiConsole.WriteLine();
				AnsiConsole.WriteLine("Press Enter to finish...");
				Console.ReadLine();
			}
		}
	}
}
