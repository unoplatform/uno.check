﻿using DotNetCheck.Models;
using NuGet.Versioning;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NuGet.Frameworks;

[assembly: InternalsVisibleTo("UnoCheck.Tests")]

namespace DotNetCheck.Cli
{
	public class CheckCommand : AsyncCommand<CheckSettings>
	{
		public override async Task<int> ExecuteAsync(CommandContext context, CheckSettings settings)
		{
			var sw = Stopwatch.StartNew();
			TelemetryClient.TrackStartCheck();

			Util.Verbose = settings.Verbose;
			Util.LogFile = settings.LogFile;
			Util.CI = settings.CI;
			if (settings.CI)
				settings.NonInteractive = true;

			Console.Title = ToolInfo.ToolName;

			AnsiConsole.Markup(AsciiAssets.UnoLogo);
			AnsiConsole.WriteLine();
			AnsiConsole.Write(
				new FigletText("uno-check").LeftJustified().Color(new Color(122, 103, 247)));

			AnsiConsole.MarkupLine($"[underline bold green]{Icon.Ambulance} {ToolInfo.ToolName} v{ToolInfo.CurrentVersion} {Icon.Recommend}[/]");
			AnsiConsole.Write(new Rule());

			AnsiConsole.MarkupLine("This tool will check your Uno Platform development environment.");
			AnsiConsole.MarkupLine("If problems are detected, it will offer the option to try and fix them for you, or suggest a way to fix them yourself.");
			AnsiConsole.Write(new Rule());

			if (await NeedsToolUpdateAsync(settings))
			{
				return 1;
			}

			if (!Util.IsAdmin() && Util.IsWindows)
			{
				var suTxt = Util.IsWindows ? "Administrator" : "Superuser (su)";

				AnsiConsole.MarkupLine($"[bold red]{Icon.Bell} {suTxt} is required to fix most issues.  Consider exiting and running the tool with {suTxt} permissions.[/]");

				AnsiConsole.Write(new Rule());

				if (!settings.NonInteractive)
				{
					if (!AnsiConsole.Confirm("Would you still like to continue?", false))
						return 1;
				}
			}

			var cts = new System.Threading.CancellationTokenSource();

			var checkupStatus = new Dictionary<string, Models.Status>();
			var sharedState = new SharedState();

			var results = new Dictionary<string, DiagnosticResult>();
			var consoleStatus = AnsiConsole.Status();

			var skippedChecks = new List<string>();

			AnsiConsole.Markup($"[bold blue]{Icon.Thinking} Synchronizing configuration...[/]");

			var channel = ManifestChannel.Default;
            if (settings.Preview)
                channel = ManifestChannel.Preview;
            if (settings.PreviewMajor)
                channel = ManifestChannel.PreviewMajor;
            if (settings.Main)
				channel = ManifestChannel.Main;

			var manifest = await ToolInfo.LoadManifest(settings.Manifest, channel);

			if (!ToolInfo.Validate(manifest))
			{
				ToolInfo.ExitPrompt(settings.NonInteractive);
				return -1;
			}

			AnsiConsole.MarkupLine(" ok");
			AnsiConsole.Markup($"[bold blue]{Icon.Thinking} Scheduling appointments...[/]");
			Util.UpdateSkips(settings, Util.BaseSkips);
			
			if (!string.IsNullOrEmpty(settings.DotNetSdkRoot))
			{
				sharedState.SetEnvironmentVariable("DOTNET_ROOT", settings.DotNetSdkRoot);
			}

			if (settings.ForceDotNet)
				sharedState.SetEnvironmentVariable("DOTNET_FORCE", "true");
			if (settings.CI)
				sharedState.SetEnvironmentVariable("CI", "true");
            if (settings.Frameworks is { Length: > 0 })
                settings.TargetPlatforms = ParseTfmsToTargetPlatforms(settings);
            if (!string.IsNullOrEmpty(settings.Ide))
            {
                switch (settings.Ide.ToLowerInvariant())
                {
                    case "rider":
	                    Util.UpdateSkips(settings, Util.RiderSkips);
                        break;
                    case "vs":
	                    Util.UpdateSkips(settings, Util.VSSkips);
                        break;
                    case "vscode":
	                    Util.UpdateSkips(settings, Util.VSCodeSkips);
                        break;
                }
            }
            
			sharedState.ContributeState(StateKey.EntryPoint, StateKey.TargetPlatforms, TargetPlatformHelper.GetTargetPlatformsFromFlags(settings.TargetPlatforms));

			var checkups = CheckupManager.BuildCheckupGraph(manifest, sharedState, settings.TargetPlatforms);

			AnsiConsole.MarkupLine(" ok");

			var checkupId = string.Empty;

			for (int i = 0; i < checkups.Count(); i++)
			{
				var checkup = checkups.ElementAt(i);

				// Set the manifest
				checkup.Manifest = manifest;

				// If the ID is the same, it's a retry
				var isRetry = checkupId == checkup.Id;

				// Track the last used id so we can detect retry
				checkupId = checkup.Id;

				if (!checkup.ShouldExamine(sharedState))
				{
					checkupStatus[checkup.Id] = Models.Status.Ok;
					continue;
				}

				var skipCheckup = false;

				var dependencies = checkup.DeclareDependencies(checkups.Select(c => c.Id));

				// Make sure our dependencies succeeded first
				if (dependencies?.Any() ?? false)
				{
					foreach (var dep in dependencies)
					{
						var depCheckup = checkups.FirstOrDefault(c => c.Id.StartsWith(dep.CheckupId, StringComparison.OrdinalIgnoreCase));

						if (depCheckup != null && depCheckup.IsPlatformSupported(Util.Platform))
						{
							if (!checkupStatus.TryGetValue(dep.CheckupId, out var depStatus) || depStatus == Models.Status.Error)
							{
								skipCheckup = dep.IsRequired;
								break;
							}
						}
					}
				}

				// See if --skip was specified
				if (settings.Skip?.Any(s => s.Equals(checkup.Id, StringComparison.OrdinalIgnoreCase)
					|| s.Equals(checkup.GetType().Name, StringComparison.OrdinalIgnoreCase)) ?? false)
					skipCheckup = true;

				if (skipCheckup)
				{
					skippedChecks.Add(checkup.Id);
					checkupStatus[checkup.Id] = Models.Status.Error;
					AnsiConsole.WriteLine();
					AnsiConsole.MarkupLine($"[bold red]{Icon.Error} Skipped: " + checkup.Title + "[/]");
					continue;
				}

				checkup.OnStatusUpdated += CheckupStatusUpdated;

				AnsiConsole.WriteLine();
				AnsiConsole.MarkupLine($"[bold]{Icon.Checking} " + checkup.Title + " Checkup[/]...");
				Console.Title = checkup.Title;

				DiagnosticResult diagnosis = null;

				try
				{
					diagnosis = await checkup.Examine(sharedState);
				}
				catch (Exception ex)
				{
					Util.Exception(ex);
					diagnosis = new DiagnosticResult(Models.Status.Error, checkup, ex.Message);
				}

				results[checkup.Id] = diagnosis;

				// Cache the status for dependencies
				checkupStatus[checkup.Id] = diagnosis.Status;

				if (diagnosis.Status == Models.Status.Ok)
					continue;

				var statusEmoji = diagnosis.Status == Models.Status.Error ? Icon.Error : Icon.Warning;
				var statusColor = diagnosis.Status == Models.Status.Error ? "red" : "darkorange3_1";

				var msg = !string.IsNullOrEmpty(diagnosis.Message) ? " - " + diagnosis.Message : string.Empty;

				if (diagnosis.HasSuggestion)
				{
					Console.WriteLine();
					AnsiConsole.Write(new Rule());
					AnsiConsole.MarkupLine($"[bold blue]{Icon.Recommend} Recommendation:[/][blue] {diagnosis.Suggestion.Name}[/]");

					if (!string.IsNullOrEmpty(diagnosis.Suggestion.Description))
						AnsiConsole.MarkupLine("" + diagnosis.Suggestion.Description + "");

					AnsiConsole.Write(new Rule());
					Console.WriteLine();

					// See if we should fix
					// needs to have a remedy available to even bother asking/trying
					var doFix = diagnosis.Suggestion.HasSolution
						&& (
							// --fix + --non-interactive == auto fix, no prompt
							(settings.NonInteractive && settings.Fix)
							// interactive (default) + prompt/confirm they want to fix
							|| (!settings.NonInteractive && AnsiConsole.Confirm($"[bold]{Icon.Bell} Attempt to fix?[/]"))
						);

					if (doFix && !isRetry)
					{
						var isAdmin = Util.IsAdmin();

						var adminMsg = Util.IsWindows ?
							$"{Icon.Bell} [red]Administrator Permissions Required.  Try opening a new console as Administrator and running this tool again.[/]"
							: $"{Icon.Bell} [red]Super User Permissions Required.  Try running this tool again with 'sudo'.[/]";

						var didFix = false;

						foreach (var remedy in diagnosis.Suggestion.Solutions)
						{
							try
							{
								remedy.OnStatusUpdated += RemedyStatusUpdated;

								AnsiConsole.MarkupLine($"{Icon.Thinking} Attempting to fix: " + checkup.Title);

								await remedy.Implement(sharedState, cts.Token);

								didFix = true;
								AnsiConsole.MarkupLine($"[bold]Fix applied.  Checking again...[/]");
							}
							catch (Exception x) when (x is AccessViolationException || x is UnauthorizedAccessException)
							{
								Util.Exception(x);
								AnsiConsole.Markup(adminMsg);
							}
							catch (Exception ex)
							{
								Util.Exception(ex);
								AnsiConsole.MarkupLine("[bold red]Fix failed - " + ex.Message + "[/]");
							}
							finally
							{
								remedy.OnStatusUpdated -= RemedyStatusUpdated;
							}
						}

						// RETRY The check again
						if (didFix)
							i--;
					}
				}

				checkup.OnStatusUpdated -= CheckupStatusUpdated;
			}

			AnsiConsole.Write(new Rule());
			AnsiConsole.WriteLine();

			var erroredChecks = results.Values.Where(d => d.Status == Models.Status.Error && !skippedChecks.Contains(d.Checkup.Id));

			foreach (var ec in erroredChecks)
				Util.Log($"Checkup had Error status: {ec.Checkup.Id}");

			var hasErrors = erroredChecks.Any();

			var warningChecks = results.Values.Where(d => d.Status == Models.Status.Warning && !skippedChecks.Contains(d.Checkup.Id));
			var hasWarnings = warningChecks.Any();

			if (hasErrors)
			{
				TelemetryClient.TrackCheckFail(sw.Elapsed, string.Join(",", erroredChecks.Select(c => c.Checkup.Id)));

				AnsiConsole.Console.WriteLine();

				foreach (var ec in erroredChecks)
					Util.Log($"{ec.Checkup.Id}: {ec.Message}");

				AnsiConsole.MarkupLine($"[bold red]{Icon.Bell} There were one or more problems detected.[/]");
				AnsiConsole.MarkupLine($"[bold red]Please review the errors and correct them and run {ToolInfo.ToolCommand} again.[/]");
			}
			else if (hasWarnings)
			{
				TelemetryClient.TrackCheckWarning(sw.Elapsed, string.Join(",", warningChecks.Select(c => c.Checkup.Id)));

				AnsiConsole.Console.WriteLine();

				foreach (var wc in warningChecks)
					Util.Log($"{wc.Checkup.Id}: {wc.Message}");

				AnsiConsole.Console.WriteLine();
				AnsiConsole.MarkupLine($"[bold darkorange3_1]{Icon.Warning} Things look almost great, except some pesky warning(s) which may or may not be a problem, but at least if they are, you'll know where to start searching![/]");
			}
			else
			{
				TelemetryClient.TrackCheckSuccess(sw.Elapsed);
                AnsiConsole.MarkupLine($"[bold blue]{Icon.Success} Congratulations, everything looks great![/]");
			}

			Console.Title = ToolInfo.ToolName;

			ToolInfo.ExitPrompt(settings.NonInteractive);

			Util.Log($"Has Errors? {hasErrors}");
			var exitCode = hasErrors ? 1 : 0;
			Environment.ExitCode = exitCode;

			return exitCode;
		}
        
        internal static string[] ParseTfmsToTargetPlatforms(CheckSettings settings)
        {
            var targetPlatforms = new List<string>();
            foreach (var tfm in settings.Frameworks!)
            {
                var parsedTfm = NuGetFramework.ParseFolder(tfm);

                // For all TFM's besides net8.0 we skip these checks.
                // https://github.com/unoplatform/private/issues/506
                if (parsedTfm.Version.Major < 9)
                {
	                var skips = settings.Skip?.ToList() ?? [];
	                settings.Skip = skips.Except(["git", "linuxninja", "psexecpolicy", "windowspyhtonInstallation"]).Distinct().ToArray();
                }
                
                if (parsedTfm.Version.Major >= 5 && parsedTfm.HasPlatform == false)
                {
                    // Returning empty list which means that we will target all platforms.
                    return [];
                } 
                if (parsedTfm.HasPlatform)
                {
                    switch (parsedTfm.Platform)
                    {
                        case "windows":
                            targetPlatforms.Add("windows");
                            break;
                        case "desktop":
                            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            {
                                targetPlatforms.Add("win32");   
                            }
                            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                            {
                                targetPlatforms.Add("macos");
                            }
                            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                            {
                                targetPlatforms.Add("linux");
                            }
                            break;
                        case "ios":
                            targetPlatforms.Add("ios");
                            break;
                        case "android":
                            targetPlatforms.Add("android");
                            break;
                        case "macos":
                        case "maccatalyst":
                            targetPlatforms.Add("macos");
                            break;
                        case "browserwasm":
                            targetPlatforms.Add("web");
                            break;
                    }
                }
                
            }
            return targetPlatforms.ToArray();
        }

		private async Task<bool> NeedsToolUpdateAsync(CheckSettings settings)
		{
			if ( (settings.Manifest is not null && !settings.CI) || Debugger.IsAttached)
			{
				return false;
			}

			bool needsToUpdate = false;
			var currentVersion = ToolInfo.CurrentVersion;
			NuGetVersion latestVersion = null;
			try
			{
				latestVersion = await ToolInfo.GetLatestVersion(currentVersion.IsPrerelease);
			}
			catch
			{
				AnsiConsole.MarkupLine($"[bold yellow]{Icon.Warning} Could not check for latest version of uno-check on NuGet.org. The currently installed version may be out of date.[/]");
				AnsiConsole.WriteLine();
				AnsiConsole.Write(new Rule());
			}

			if (latestVersion is not null && currentVersion < latestVersion)
			{
				AnsiConsole.MarkupLine($"[bold yellow]{Icon.Warning} Your uno-check version is not up to date. The latest version is {latestVersion}. You can use the following command to update:[/]");
				AnsiConsole.WriteLine();
				AnsiConsole.MarkupLine($"dotnet tool update --global Uno.Check --version {latestVersion}");
				AnsiConsole.WriteLine();

				AnsiConsole.Write(new Rule());
			}

			if (!settings.CI && (latestVersion is null || currentVersion < latestVersion))
			{
				var shouldContinue = AnsiConsole.Confirm("Would you still like to continue with the currently installed version?", false);
				needsToUpdate = !shouldContinue;
            }

			return needsToUpdate;
		}

		private void CheckupStatusUpdated(object sender, CheckupStatusEventArgs e)
		{
			var msg = "";
			if (e.Status == Models.Status.Error)
				msg = $"[red]{Icon.Error} {e.Message}[/]";
			else if (e.Status == Models.Status.Warning)
				msg = $"[darkorange3_1]{Icon.Warning} {e.Message}[/]";
			else if (e.Status == Models.Status.Ok)
				msg = $"[green]{Icon.Success} {e.Message}[/]";
			else
				msg = $"{Icon.ListItem} {e.Message}";

			AnsiConsole.MarkupLine("  " + msg);
		}
        
		private void RemedyStatusUpdated(object sender, RemedyStatusEventArgs e)
		{
			AnsiConsole.MarkupLine("  " + e.Message);
		}
	}
}
