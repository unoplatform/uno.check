#nullable enable
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
		internal const string MainManifestUrl = "https://raw.githubusercontent.com/unoplatform/uno.check/main/manifests/uno.ui.manifest.json";

		public static Task<Manifest.Manifest> LoadManifest(string? fileOrUrl, ManifestChannel channel)
			=> LoadManifest(fileOrUrl, channel, mainManifestUrlOverride: null);

		internal static async Task<Manifest.Manifest> LoadManifest(string? fileOrUrl, ManifestChannel channel, string? mainManifestUrlOverride)
		{
			// If a specific file or URL is provided, use it
			if (!string.IsNullOrEmpty(fileOrUrl))
			{
				Util.Log($"Loading Manifest from: {fileOrUrl}");
				return await Manifest.Manifest.FromFileOrUrl(fileOrUrl);
			}

			if (channel == ManifestChannel.Main)
			{
				var mainManifestUrl = mainManifestUrlOverride is null ? MainManifestUrl : mainManifestUrlOverride;

				if (!string.IsNullOrWhiteSpace(mainManifestUrl))
				{
					try
					{
						Util.Log($"Loading Main Manifest from URL: {mainManifestUrl}");
						return await Manifest.Manifest.FromFileOrUrl(mainManifestUrl);
					}
					catch (Exception ex)
					{
						Util.Log($"Failed loading Main Manifest from URL: {mainManifestUrl}. Falling back to embedded stable manifest.");
						Util.Exception(ex);
						AnsiConsole.MarkupLine($"[bold yellow]{Icon.Warning} Could not load main manifest from configured source. Falling back to embedded stable manifest.[/]");
					}
				}
				else
				{
					Util.Log("Main Manifest URL is not configured. Falling back to embedded stable manifest.");
				}
			}

			// Otherwise, load from embedded resources based on the channel
			var resourceName = channel switch
			{
				ManifestChannel.Preview => Manifest.Manifest.PreviewManifestResourceName,
				ManifestChannel.PreviewMajor => Manifest.Manifest.PreviewMajorManifestResourceName,
				ManifestChannel.Main => Manifest.Manifest.DefaultManifestResourceName,
				ManifestChannel.Default => Manifest.Manifest.DefaultManifestResourceName,
				_ => Manifest.Manifest.DefaultManifestResourceName
			};

			Util.Log($"Loading Manifest from embedded resource: {resourceName}");

			return await Manifest.Manifest.FromEmbeddedResource(resourceName);
		}

		public static NuGetVersion CurrentVersion
			=> NuGetVersion.Parse(FileVersionInfo.GetVersionInfo(typeof(ToolInfo).Assembly.Location).FileVersion);

		public static bool Validate(Manifest.Manifest manifest, bool strictManifest = false)
		{
			var toolVersion = manifest?.Check?.ToolVersion;

			Util.Log($"Required Version: {toolVersion}");

			var fileVersion = CurrentVersion;

			Util.Log($"Current Version: {fileVersion}");

			if (string.IsNullOrWhiteSpace(toolVersion))
			{
				AnsiConsole.WriteLine();
				AnsiConsole.MarkupLine($"[bold yellow]{Icon.Warning} Uno.Check manifest version check failed.[/]");
				AnsiConsole.MarkupLine("[yellow]The manifest does not define a required Uno.Check tool version (check.toolVersion).[/]");
				AnsiConsole.MarkupLine("[yellow]Consider using a known-good manifest via the --manifest option, switching to the stable manifest channel, or asking the manifest maintainer to define a valid semantic version for check.toolVersion.[/]");

				if (strictManifest)
				{
					AnsiConsole.MarkupLine($"[bold red]{Icon.Error} Strict manifest mode is enabled (CI). Aborting.[/]");
					return false;
				}

				AnsiConsole.MarkupLine("[yellow]Continuing in non-strict mode. Results may be unreliable until the manifest is corrected.[/]");
			}
			else if (!NuGetVersion.TryParse(toolVersion, out var toolVer))
			{
				AnsiConsole.WriteLine();
				AnsiConsole.MarkupLine($"[bold yellow]{Icon.Warning} Uno.Check manifest version check failed.[/]");
				AnsiConsole.MarkupLine($"[yellow]The manifest requires an invalid Uno.Check version format: {Markup.Escape(toolVersion!)}[/]");
				AnsiConsole.MarkupLine("[yellow]Please fix the manifest 'check.toolVersion' value to a valid semantic version.[/]");

				if (strictManifest)
				{
					AnsiConsole.MarkupLine($"[bold red]{Icon.Error} Strict manifest mode is enabled (CI). Aborting.[/]");
					return false;
				}

				AnsiConsole.MarkupLine("[yellow]Continuing in non-strict mode. Results may be unreliable until the manifest is corrected.[/]");
			}
			else if (fileVersion < toolVer)
			{
				AnsiConsole.WriteLine();
				AnsiConsole.MarkupLine($"[bold yellow]{Icon.Warning} Uno.Check version support mismatch detected.[/]");
				AnsiConsole.MarkupLine($"[yellow]Required by manifest: {Markup.Escape(toolVersion!)} | Current: {fileVersion}[/]");
				AnsiConsole.MarkupLine($"[blue]Update command: dotnet tool update --global {ToolPackageId}[/]");

				if (strictManifest)
				{
					AnsiConsole.MarkupLine($"[bold red]{Icon.Error} Strict manifest mode is enabled (CI). Aborting.[/]");
					return false;
				}

				AnsiConsole.MarkupLine($"[yellow]Continuing in non-strict mode. Results may be unreliable until Uno.Check is updated.[/]");
			}

			var minSupportedDotNetSdkVersion = Manifest.DotNetSdk.Version6Preview7;

			// Check that we aren't on a manifest that wants too old of dotnet6
			if (manifest?.Check?.DotNet?.Sdks?.Any(dnsdk =>
				NuGetVersion.TryParse(dnsdk.Version, out var dnsdkVersion) && dnsdkVersion < minSupportedDotNetSdkVersion) ?? false)
			{
				AnsiConsole.WriteLine();
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
