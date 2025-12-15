using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DotNetCheck
{
	/// <summary>
	/// Provides interactive selection prompts for IDE and target platforms
	/// </summary>
	internal static class InteractiveSelector
	{
		/// <summary>
		/// Determines if interactive prompts should be shown based on settings and environment
		/// </summary>
		public static bool ShouldShowInteractivePrompts(CheckSettings settings)
		{
			// Don't show in CI or non-interactive mode
			if (settings.CI || settings.NonInteractive)
				return false;

			// Don't show if any relevant flags are already specified
			if (!string.IsNullOrEmpty(settings.Ide))
				return false;
			
			if (settings.TargetPlatforms?.Any() == true)
				return false;
			
			if (settings.Frameworks?.Any() == true)
				return false;
			
			if (settings.Skip?.Any() == true)
				return false;

			return true;
		}

		/// <summary>
		/// Prompts user to select their IDE
		/// </summary>
		public static string PromptForIde()
		{
			var ideChoices = new List<string>();

			// Show platform-appropriate IDEs
			if (Util.IsWindows)
			{
				ideChoices.Add("Visual Studio");
				ideChoices.Add("VS Code");
				ideChoices.Add("Rider");
				ideChoices.Add("Other");
				ideChoices.Add("None");
			}
			else if (Util.IsMac)
			{
				ideChoices.Add("VS Code");
				ideChoices.Add("Rider");
				ideChoices.Add("Other");
				ideChoices.Add("None");
			}
			else // Linux
			{
				ideChoices.Add("VS Code");
				ideChoices.Add("Rider");
				ideChoices.Add("Other");
				ideChoices.Add("None");
			}

			var selectedIde = AnsiConsole.Prompt(
				new SelectionPrompt<string>()
					.Title("[bold blue]Which IDE do you plan to use?[/]")
					.AddChoices(ideChoices)
					.HighlightStyle(new Style(Color.Green)));

			// Map friendly name to internal identifier
			return selectedIde switch
			{
				"Visual Studio" => "vs",
				"VS Code" => "vscode",
				"Rider" => "rider",
				"Other" => "other",
				"None" => "none",
				_ => "other"
			};
		}

		/// <summary>
		/// Prompts user to select target platforms
		/// </summary>
		public static string[] PromptForTargetPlatforms()
		{
			var platformChoices = new List<string>();

			// Show platform-appropriate targets
			if (Util.IsWindows)
			{
				platformChoices.Add("Windows");
				platformChoices.Add("Android");
				platformChoices.Add("iOS");
				platformChoices.Add("WebAssembly");
				platformChoices.Add("Desktop (Skia)");
			}
			else if (Util.IsMac)
			{
				platformChoices.Add("macOS");
				platformChoices.Add("iOS");
				platformChoices.Add("Android");
				platformChoices.Add("WebAssembly");
				platformChoices.Add("Desktop (Skia)");
			}
			else // Linux
			{
				platformChoices.Add("Linux");
				platformChoices.Add("Android");
				platformChoices.Add("WebAssembly");
				platformChoices.Add("Desktop (Skia)");
			}

			var selectedPlatforms = AnsiConsole.Prompt(
				new MultiSelectionPrompt<string>()
					.Title("[bold blue]Which platforms do you want to target?[/]")
					.PageSize(10)
					.MoreChoicesText("[grey](Move up and down to reveal more platforms)[/]")
					.InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
					.AddChoices(platformChoices)
					.HighlightStyle(new Style(Color.Green)));

			// If no platforms selected, return empty array (will target all)
			if (!selectedPlatforms.Any())
				return Array.Empty<string>();

			// Map friendly names to internal identifiers
			return selectedPlatforms.Select(p => p switch
			{
				"Windows" => "windows",
				"macOS" => "macos",
				"iOS" => "ios",
				"Android" => "android",
				"WebAssembly" => "webassembly",
				"Linux" => "linux",
				"Desktop (Skia)" => "desktop",
				_ => p.ToLowerInvariant()
			}).ToArray();
		}

		/// <summary>
		/// Applies interactive selections to settings
		/// </summary>
		public static void ApplyInteractiveSelections(CheckSettings settings)
		{
			if (!ShouldShowInteractivePrompts(settings))
				return;

			AnsiConsole.WriteLine();
			AnsiConsole.Write(new Rule("[bold yellow]Interactive Setup[/]").LeftJustified());
			AnsiConsole.MarkupLine("[grey]Let's customize the checks for your development environment.[/]");
			AnsiConsole.WriteLine();

			// Prompt for IDE
			var ide = PromptForIde();
			settings.Ide = ide;

			AnsiConsole.WriteLine();

			// Prompt for target platforms
			var platforms = PromptForTargetPlatforms();
			if (platforms.Any())
			{
				settings.TargetPlatforms = platforms;
			}

			AnsiConsole.WriteLine();
			AnsiConsole.Write(new Rule().LeftJustified());
		}
	}
}
