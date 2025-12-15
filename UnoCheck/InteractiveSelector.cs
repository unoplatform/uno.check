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
		/// Prompts user to select their IDE(s)
		/// </summary>
		/// <returns>
		/// Array of IDE identifiers. If no IDEs selected, returns empty array and no IDE checks will be skipped.
		/// </returns>
		public static string[] PromptForIde()
		{
			var ideChoices = new List<string>();

			// Show platform-appropriate IDEs
			if (Util.IsWindows)
			{
				ideChoices.Add("Visual Studio");
			}
			
			// Common IDEs across all platforms
			ideChoices.Add("VS Code");
			ideChoices.Add("Rider");
			ideChoices.Add("Other");

			var selectedIdes = AnsiConsole.Prompt(
				new MultiSelectionPrompt<string>()
					.Title("[bold blue]Which IDE(s) do you plan to use?[/]")
					.PageSize(10)
					.InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept. Selecting nothing will skip IDE checks)[/]")
					.AddChoices(ideChoices)
					.HighlightStyle(new Style(Color.Green)));

			// If no IDEs selected, return empty array (no IDE-specific checks will be skipped)
			if (!selectedIdes.Any())
				return Array.Empty<string>();

			// Map friendly names to internal identifiers
			return selectedIdes.Select(ide => ide switch
			{
				"Visual Studio" => "vs",
				"VS Code" => "vscode",
				"Rider" => "rider",
				"Other" => "other",
				_ => "other"
			}).ToArray();
		}

		/// <summary>
		/// Prompts user to select target platforms
		/// </summary>
		/// <returns>
		/// Array of platform identifiers. If empty, all platforms will be targeted.
		/// </returns>
		public static string[] PromptForTargetPlatforms()
		{
			var platformChoices = new List<string>();

			// Platform order: desktop, wasm, ios, android, winappsdk
			platformChoices.Add("Desktop");
			platformChoices.Add("WebAssembly");
			
			// iOS is available on Windows and Mac only (not on Linux)
			if (Util.IsWindows || Util.IsMac)
			{
				platformChoices.Add("iOS");
			}
			
			platformChoices.Add("Android");
			
			// Windows App SDK only on Windows
			if (Util.IsWindows)
			{
				platformChoices.Add("Windows App SDK");
			}

			var selectedPlatforms = AnsiConsole.Prompt(
				new MultiSelectionPrompt<string>()
					.Title("[bold blue]Which platforms do you want to target?[/]")
					.PageSize(10)
					.InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept. Select none to target all platforms)[/]")
					.AddChoices(platformChoices)
					.HighlightStyle(new Style(Color.Green)));

			// If no platforms selected, return empty array (will target all)
			if (!selectedPlatforms.Any())
				return Array.Empty<string>();

			// Map friendly names to internal identifiers
			return selectedPlatforms.Select(p => p switch
			{
				"Desktop" => "desktop",
				"WebAssembly" => "webassembly",
				"iOS" => "ios",
				"Android" => "android",
				"Windows App SDK" => "windows",
				// Fallback should not happen with controlled selection list
				_ => throw new InvalidOperationException($"Unexpected platform selection: {p}")
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

			// Prompt for IDE(s)
			var ides = PromptForIde();
			
			// Apply IDE selection
			if (ides.Any())
			{
				settings.Ide = ides[0];
				AnsiConsole.MarkupLine($"[grey]Selected IDE(s): {string.Join(", ", ides)}[/]");
			}
			else
			{
				AnsiConsole.MarkupLine("[grey]No IDE selected - IDE checks will be skipped[/]");
			}

			AnsiConsole.WriteLine();

			// Prompt for target platforms
			var platforms = PromptForTargetPlatforms();
			if (platforms.Any())
			{
				settings.TargetPlatforms = platforms;
				AnsiConsole.MarkupLine($"[grey]Selected platforms: {string.Join(", ", platforms)}[/]");
			}
			else
			{
				AnsiConsole.MarkupLine("[grey]No platforms selected - all platforms will be targeted[/]");
			}

			AnsiConsole.WriteLine();
			AnsiConsole.Write(new Rule().LeftJustified());
		}
	}
}
