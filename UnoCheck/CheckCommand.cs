using DotNetCheck.Models;
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

			// Structured mode must claim stdout before anything else can write to it —
			// even telemetry can print (in DEBUG) and would corrupt the JSONL stream.
			if (settings.Json || !string.IsNullOrEmpty(settings.JsonFile))
			{
				settings.NonInteractive = true;
				Json.JsonlOutput.Init(settings.Json ? Console.Out : null, settings.JsonFile, settings.CorrelationId);

				// If the only requested sink could not be created (e.g. --json-file pointing
				// at an existing path), proceeding would run completely blind: no events and
				// no terminal report, while the host tails a file that never gets written.
				// Fail fast instead — the warning is already on stderr, and the host gets an
				// immediate non-zero exit rather than an endless wait.
				if (!Json.JsonlOutput.Enabled)
				{
					Environment.ExitCode = -1;
					return -1;
				}

				if (settings.Json)
				{
					// stdout carries pure JSONL: the event stream owns the real stdout
					// (captured by Init above), and everything else — Spectre and any
					// direct Console.Out writer anywhere in the process — is rerouted
					// to stderr so no stray write can corrupt the stream.
					Console.SetOut(Console.Error);
					AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
					{
						Ansi = AnsiSupport.Detect,
						Out = new AnsiConsoleOutput(Console.Error),
					});
				}
			}

			TelemetryClient.TrackStartCheck(settings.Frameworks);

			Util.Verbose = settings.Verbose;
			Util.LogFile = settings.LogFile;
			Util.CI = settings.CI;
			if (settings.CI)
				settings.NonInteractive = true;

			Util.NonInteractive = settings.NonInteractive;

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

			// Structured-output consumers (GUI hosts, CI, agents) pin the tool version themselves;
			// the interactive update prompt would corrupt the event stream.
			var structuredMode = settings.Json || !string.IsNullOrEmpty(settings.JsonFile);
			if (!structuredMode && await ToolUpdater.CheckAndPromptForUpdateAsync(settings))
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

			using var cts = new System.Threading.CancellationTokenSource();
			ConsoleCancelEventHandler cancelHandler = (sender, args) =>
			{
				if (!cts.IsCancellationRequested)
				{
					args.Cancel = true;
					cts.Cancel();
					AnsiConsole.MarkupLine($"[bold yellow]{Icon.Warning} Cancellation requested. Stopping current operation...[/]");
					AnsiConsole.MarkupLine($"[yellow]You can resume later by rerunning {ToolInfo.ToolCommand} --fix.[/]");
				}
				else
				{
					args.Cancel = false;
				}
			};
			Console.CancelKeyPress += cancelHandler;

			// Hoisted out of the try so the finally can always emit a terminal report:
			// hosts wait for the report event as the end-of-stream marker, and with
			// --json-file there is no stdout close to fall back on.
			var reportChecks = new Dictionary<string, Json.HealthCheck>();
			var reportEmitted = false;
			string abnormalReason = null;

			try
			{

			var checkupStatus = new Dictionary<string, Models.Status>();
			var sharedState = new SharedState();

			var results = new Dictionary<string, DiagnosticResult>();
			var consoleStatus = AnsiConsole.Status();

            var skippedChecks = new List<string>();
            var skippedFix = new List<string>();

            AnsiConsole.Markup($"[bold blue]{Icon.Thinking} Synchronizing configuration...[/]");

			var channel = ManifestChannel.Default;
            if (settings.Preview)
                channel = ManifestChannel.Preview;
            if (settings.PreviewMajor)
                channel = ManifestChannel.PreviewMajor;
            if (settings.Main)
				channel = ManifestChannel.Main;

			var manifest = await ToolInfo.LoadManifest(settings.Manifest, channel);

			var strictManifest = settings.CI;

			if (!ToolInfo.Validate(manifest, strictManifest))
			{
				abnormalReason = "manifest validation failed";
				ToolInfo.ExitPrompt(settings.NonInteractive);
				return -1;
			}

			AnsiConsole.MarkupLine(" ok");
			AnsiConsole.Markup($"[bold blue]{Icon.Thinking} Scheduling appointments...[/]");

			SkipInfo[] skipList = (settings.Skip ?? [])
				.Select(s => new SkipInfo(s, "Skipped by command line", false))
				.Concat(Util.BaseSkips.Select(s => new SkipInfo(s, "Not required by the current configuration", false)))
				.Distinct(SkipInfo.NameOnlyComparer)
				.ToArray();
			
			if (!string.IsNullOrEmpty(settings.DotNetSdkRoot))
			{
				sharedState.SetEnvironmentVariable("DOTNET_ROOT", settings.DotNetSdkRoot);
			}
			else if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { } dotnetRoot)
			{
				sharedState.SetEnvironmentVariable("DOTNET_ROOT", dotnetRoot);
			}

			if (settings.ForceDotNet)
				sharedState.SetEnvironmentVariable("DOTNET_FORCE", "true");
			if (settings.CI)
				sharedState.SetEnvironmentVariable("CI", "true");
			if (!string.IsNullOrEmpty(settings.UnoSdkVersion))
				sharedState.SetEnvironmentVariable("UnoSdkVersion", settings.UnoSdkVersion);
            if (settings.Frameworks is { Length: > 0 })
                settings.TargetPlatforms = ParseTfmsToTargetPlatforms(settings);

            if (!string.IsNullOrEmpty(settings.Ide))
            {
                skipList = skipList.Concat(
					(
						settings.Ide.ToLowerInvariant() switch
						{
							"rider" => Util.RiderSkips,
							"vs" => Util.VSSkips,
							"vscode" => Util.VSCodeSkips,
							_ => []
						}
					)
					.Select(s => new SkipInfo(s, "Not required by the current configuration", false))
				)
				.Distinct(SkipInfo.NameOnlyComparer)
				.ToArray();
            }

			sharedState.ContributeState(StateKey.EntryPoint, StateKey.Skips, skipList);
            
			sharedState.ContributeState(StateKey.EntryPoint, StateKey.TargetPlatforms, TargetPlatformHelper.GetTargetPlatformsFromFlags(settings.TargetPlatforms));

			var checkups = ApplyOnlyFilter(
				CheckupManager.BuildCheckupGraph(manifest, sharedState, settings.TargetPlatforms).ToList(),
				settings.Only,
				out var unknownOnlyIds);

			AnsiConsole.MarkupLine(" ok");

			// A typo'd --only must never produce a passing empty run.
			if (unknownOnlyIds.Count > 0)
			{
				var unknownList = string.Join(", ", unknownOnlyIds);
				AnsiConsole.MarkupLine($"[bold red]{Icon.Error} Unknown checkup id(s) for --only: {Markup.Escape(unknownList)}. Run '{ToolInfo.ToolCommand} list' to see available checkup ids.[/]");
				abnormalReason = $"unknown checkup id(s) for --only: {unknownList}";
				Environment.ExitCode = 1;
				return 1;
			}

			Json.JsonlOutput.Emit(new Json.RunStartedEvent
			{
				ToolVersion = ToolInfo.CurrentVersion.ToString(),
				Channel = channel.ToString(),
				Targets = settings.TargetPlatforms,
				CheckupCount = checkups.Count,
			});

			var checkupId = string.Empty;

			for (int i = 0; i < checkups.Count; i++)
			{
				var checkup = checkups[i];

				// Ctrl+C during a checkup's Examine cannot be observed (its signature takes
				// no token), but the loop honors cancellation between checkups and the
				// finally below guarantees the terminal report either way.
				if (cts.IsCancellationRequested)
				{
					abnormalReason = "canceled";
					Environment.ExitCode = 130;
					return 130;
				}

				// Set the manifest
				checkup.Manifest = manifest;

				// If the ID is the same, it's a retry
				var isRetry = checkupId == checkup.Id;

				// Track the last used id so we can detect retry
				checkupId = checkup.Id;

				if (!checkup.ShouldExamine(sharedState))
				{
					checkupStatus[checkup.Id] = Models.Status.Ok;

					// Announced in checkup_count, so it must resolve: report it as skipped
					// rather than vanishing (hosts build progress off checkup_count).
					var notApplicable = new Json.HealthCheck
					{
						Id = checkup.Id,
						Name = checkup.Title,
						Status = Json.JsonlOutput.StatusSkipped,
						SkipReason = "Not applicable to this environment",
					};
					reportChecks[checkup.Id] = notApplicable;
					Json.JsonlOutput.Emit(new Json.CheckupResultEvent { Check = notApplicable });

					continue;
				}

				SkipInfo skipCheckup = null;

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
								if (dep.IsRequired)
								{
                                    skipCheckup = new(checkup.Id, $"The dependent check {dep.CheckupId} is required first", true);
								}
								break;
							}
						}
					}
				}

                // See if --skip was specified
                if(skipList?.FirstOrDefault(s => 
					s.CheckupId.Equals(checkup.Id, StringComparison.OrdinalIgnoreCase)
					|| s.CheckupId.Equals(checkup.GetType().Name, StringComparison.OrdinalIgnoreCase)) is { } explicitSkip)
				{
                    skipCheckup = explicitSkip;
                }

				if (skipCheckup is not null)
				{
					skippedChecks.Add(checkup.Id);
					checkupStatus[checkup.Id] = skipCheckup.isError ? Models.Status.Error : Models.Status.Ok;
					AnsiConsole.WriteLine();

					var icon = skipCheckup.isError
						? $"[bold red]{Icon.Error}"
						: $"[bold gray]{Icon.Ignored}";

					AnsiConsole.MarkupLine($"{icon} Skipped: {Markup.Escape(checkup.Title)} ({Markup.Escape(skipCheckup.skipReason)})[/]");

					// Wire status is always "skipped" — the checkup was not examined. When the
					// skip stems from a failed dependency, that dependency's own "error" result
					// is what makes the run unhealthy; the skip must not double-report it.
					var skippedCheck = new Json.HealthCheck
					{
						Id = checkup.Id,
						Name = checkup.Title,
						Status = Json.JsonlOutput.StatusSkipped,
						SkipReason = skipCheckup.skipReason,
					};
					reportChecks[checkup.Id] = skippedCheck;
					Json.JsonlOutput.Emit(new Json.CheckupResultEvent { Check = skippedCheck });

					continue;
				}

				checkup.OnStatusUpdated += CheckupStatusUpdated;

				AnsiConsole.WriteLine();
				AnsiConsole.MarkupLine($"[bold]{Icon.Checking} {Markup.Escape(checkup.Title)} Checkup[/]...");
				Console.Title = checkup.Title;

				if (!isRetry)
					Json.JsonlOutput.Emit(new Json.CheckupStartedEvent { Id = checkup.Id, Name = checkup.Title });

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

				var healthCheck = BuildHealthCheck(checkup, diagnosis);
				reportChecks[checkup.Id] = healthCheck;

				// On a post-fix retry this emits a second checkup_result for the same id
				// (with no second checkup_started); the contract is last-result-per-id-wins.
				Json.JsonlOutput.Emit(new Json.CheckupResultEvent { Check = healthCheck });

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
						AnsiConsole.MarkupLine(diagnosis.Suggestion.Description);

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

					if(!doFix && !isRetry)
					{
						skippedFix.Add(checkup.Id);
					}

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
								_activeFixCheckupId = checkup.Id;

								AnsiConsole.MarkupLine($"{Icon.Thinking} Attempting to fix: {Markup.Escape(checkup.Title)}");

								Json.JsonlOutput.Emit(new Json.FixStartedEvent { Id = checkup.Id, Solution = remedy.GetType().Name });

								await remedy.Implement(sharedState, cts.Token);

								didFix = true;
								AnsiConsole.MarkupLine($"[bold]Fix applied.  Checking again...[/]");

								Json.JsonlOutput.Emit(new Json.FixResultEvent { Id = checkup.Id, Success = true });
							}
							catch (Exception x) when (x is AccessViolationException || x is UnauthorizedAccessException)
							{
								Util.Exception(x);
								AnsiConsole.Markup(adminMsg);

								Json.JsonlOutput.Emit(new Json.FixResultEvent { Id = checkup.Id, Success = false, Error = $"Elevation required: {x.Message}" });
							}
							catch (OperationCanceledException)
							{
								AnsiConsole.MarkupLine($"[bold yellow]{Icon.Warning} Operation canceled by user.[/]");

								Json.JsonlOutput.Emit(new Json.FixResultEvent { Id = checkup.Id, Success = false, Error = "Canceled" });

								abnormalReason = "canceled";
								Environment.ExitCode = 130;
								return 130;
							}
							catch (Exception ex)
							{
								Util.Exception(ex);
								AnsiConsole.MarkupLine($"[bold red]Fix failed - {Markup.Escape(ex.Message)}[/]");

								Json.JsonlOutput.Emit(new Json.FixResultEvent { Id = checkup.Id, Success = false, Error = ex.Message });
							}
							finally
							{
								remedy.OnStatusUpdated -= RemedyStatusUpdated;
								_activeFixCheckupId = null;
							}
						}

						// RETRY The check again
						if (didFix)
							i--;
					}
				}
				else if (diagnosis.Status != Models.Status.Ok && diagnosis.Message is { Length: > 0 } m)
				{
					// Display error/warning message when there's no suggestion
					Console.WriteLine();
					AnsiConsole.MarkupLine($"[bold {statusColor}]{statusEmoji} {Markup.Escape(m)}[/]");
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
				TelemetryClient.TrackCheckFail(
					sw.Elapsed,
					string.Join(",", erroredChecks.Select(c => (skippedFix.Contains(c.Checkup.Id) ? "~" : "") + c.Checkup.Id)));

				AnsiConsole.Console.WriteLine();

				foreach (var ec in erroredChecks)
					Util.Log($"{ec.Checkup.Id}: {ec.Message}");

				AnsiConsole.MarkupLine($"[bold red]{Icon.Bell} There were one or more problems detected.[/]");
				AnsiConsole.MarkupLine($"[bold red]Please review the errors and correct them and run {ToolInfo.ToolCommand} again.[/]");
				AnsiConsole.MarkupLine($"[bold red]For more details about the errors, rerun the command with --verbose.[/]");
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

			Json.JsonlOutput.Emit(new Json.ReportEvent
			{
				Report = Json.JsonlOutput.BuildReport(
					ToolInfo.CurrentVersion.ToString(),
					hasErrors ? "unhealthy" : hasWarnings ? "degraded" : "healthy",
					reportChecks.Values),
			});
			reportEmitted = true;

			Console.Title = ToolInfo.ToolName;

			ToolInfo.ExitPrompt(settings.NonInteractive);

			Util.Log($"Has Errors? {hasErrors}");
			var exitCode = hasErrors ? 1 : 0;
			Environment.ExitCode = exitCode;

			return exitCode;
			}
			catch (Exception ex)
			{
				// Hosts tailing --json-file cannot see stderr: carry the failure into the
				// terminal report before Spectre's handler renders and rethrows it.
				abnormalReason ??= $"unhandled exception: {ex.Message}";
				throw;
			}
			finally
			{
				// Guarantee the end-of-stream marker on every exit path — early returns
				// (manifest validation, unknown --only ids, cancellation) and unhandled
				// exceptions alike. Hosts otherwise wait forever on a stream with no report.
				if (Json.JsonlOutput.Enabled && !reportEmitted)
				{
					Json.JsonlOutput.Emit(new Json.ReportEvent
					{
						Report = Json.JsonlOutput.BuildReport(
							ToolInfo.CurrentVersion.ToString(),
							"unhealthy",
							reportChecks.Values,
							abnormalReason ?? "aborted before completion"),
					});
				}

				Console.CancelKeyPress -= cancelHandler;
			}
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
                                targetPlatforms.Add("skiadesktop");
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
                        case "tvos":
                            targetPlatforms.Add("tvos");
                            break;
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

		private string _activeFixCheckupId;

		/// <summary>
		/// --only: scope the run to the requested checkup(s) plus their required dependencies.
		/// Caller-supplied ids match exactly (case-insensitive) so a per-item fix can never
		/// select siblings the caller did not name. Ids declared by dependencies keep the
		/// one-way prefix rule the checkup loop uses (a declared "dotnetworkloads" matches
		/// the versioned "dotnetworkloads-&lt;ver&gt;" checkup). Caller ids that match nothing
		/// (including empty entries) come back in <paramref name="unknownIds"/> so the run
		/// can fail loudly instead of passing empty. Returns the list unchanged when no ids
		/// are given.
		/// </summary>
#nullable enable
		internal static List<Checkup> ApplyOnlyFilter(List<Checkup> checkups, string[]? only, out List<string> unknownIds)
		{
			unknownIds = new List<string>();

			if (only is not { Length: > 0 })
				return checkups;

			unknownIds.AddRange(only.Where(string.IsNullOrWhiteSpace).Select(_ => "(empty)"));

			var userIds = new HashSet<string>(
				only.Where(o => !string.IsNullOrWhiteSpace(o)),
				StringComparer.OrdinalIgnoreCase);

			var depIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			bool Matches(string checkupId)
				=> userIds.Contains(checkupId)
					|| depIds.Any(d => checkupId.StartsWith(d, StringComparison.OrdinalIgnoreCase));

			bool expanded;
			do
			{
				expanded = false;
				var allIds = checkups.Select(c => c.Id).ToArray();

				foreach (var c in checkups.Where(c => Matches(c.Id)))
				{
					foreach (var dep in c.DeclareDependencies(allIds).Where(d => d.IsRequired))
					{
						if (depIds.Add(dep.CheckupId))
							expanded = true;
					}
				}
			} while (expanded);

			unknownIds.AddRange(userIds.Where(u => !checkups.Any(c => c.Id.Equals(u, StringComparison.OrdinalIgnoreCase))));

			return checkups.Where(c => Matches(c.Id)).ToList();
		}
#nullable restore

		internal static Json.HealthCheck BuildHealthCheck(Checkup checkup, DiagnosticResult diagnosis)
		{
			Json.FixInfo fix = null;

			if (diagnosis.Status != Models.Status.Ok && diagnosis.HasSuggestion)
			{
				// The id flows into a host-executed (often elevated) fix invocation.
				// Workload checkup ids embed a manifest/environment-sourced version string,
				// so only vouch for ids that cannot smuggle argument or shell metacharacters,
				// and hand hosts an argument vector — never a pre-joined command string.
				var fixable = diagnosis.Suggestion.HasSolution && Json.JsonlOutput.IsSafeCheckupId(checkup.Id);

				fix = new Json.FixInfo
				{
					IssueId = checkup.Id,
					Description = string.IsNullOrEmpty(diagnosis.Suggestion.Description)
						? diagnosis.Suggestion.Name
						: $"{diagnosis.Suggestion.Name}: {diagnosis.Suggestion.Description}",
					AutoFixable = fixable,
					Args = fixable
						? new[] { "--fix", "--only", checkup.Id, "--non-interactive" }
						: null,
				};
			}

			return new Json.HealthCheck
			{
				Id = checkup.Id,
				Name = checkup.Title,
				Status = Json.JsonlOutput.StatusName(diagnosis.Status),
				Message = diagnosis.Message,
				Fix = fix,
			};
		}

		private void CheckupStatusUpdated(object sender, CheckupStatusEventArgs e)
		{
			AnsiConsole.MarkupLine("  " + BuildCheckupStatusMarkup(e.Message, e.Status));

			if (Json.JsonlOutput.Enabled)
			{
				Json.JsonlOutput.Emit(new Json.CheckupProgressEvent
				{
					Id = e.Checkup?.Id,
					Message = e.Message,
					Status = e.Status.HasValue ? Json.JsonlOutput.StatusName(e.Status.Value) : null,
					Progress = e.Progress >= 0 ? e.Progress : (int?)null,
				});
			}
		}

#nullable enable
		internal static string BuildCheckupStatusMarkup(string? message, Models.Status? status)
		{
			var escaped = Markup.Escape(message ?? string.Empty);
			if (status == Models.Status.Error)
				return $"[red]{Icon.Error} {escaped}[/]";
			if (status == Models.Status.Warning)
				return $"[darkorange3_1]{Icon.Warning} {escaped}[/]";
			if (status == Models.Status.Ok)
				return $"[green]{Icon.Success} {escaped}[/]";
			return $"{Icon.ListItem} {escaped}";
		}
#nullable restore

		private void RemedyStatusUpdated(object sender, RemedyStatusEventArgs e)
		{
			AnsiConsole.MarkupLine("  " + Markup.Escape(e.Message));

			if (Json.JsonlOutput.Enabled)
				Json.JsonlOutput.Emit(new Json.FixProgressEvent { Id = _activeFixCheckupId, Message = e.Message });
		}
    }
}
