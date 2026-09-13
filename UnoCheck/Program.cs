using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DotNetCheck.Checkups;
using DotNetCheck.Cli;
using DotNetCheck.Models;
using Spectre.Console.Cli;

namespace DotNetCheck
{
	internal class Program
	{
		static Task<int> Main(string[] args)
		{
			TelemetryClient.Init();
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				// In structured-output mode a host process (GUI, CI, agent) owns the UX:
				// hide the console instead of fronting it. Checked on raw args because
				// the window must be handled before command-line parsing runs. Only a
				// console this process owns is ever hidden — a run typed into an existing
				// terminal shares that window, and hiding it would take out the user's shell.
				if (Json.JsonlOutput.IsStructuredOutputRequested(args))
				{
					if (ConsoleWindowHelpers.OwnsConsole())
						ConsoleWindowHelpers.Hide();
				}
				else
				{
					ConsoleWindowHelpers.BringToFront();
				}
			}

			// Claim stdout for the event stream before the command app exists. Argument
			// parsing can fail first — an unknown --only value, a missing --manifest — and the
			// command infrastructure writes those errors to the stdout it captured when it was
			// built. Redirecting inside the command is too late: the host would get a JSON
			// report followed by plain text on the same stream.
			if (Json.JsonlOutput.IsStdoutStreamRequested(args))
			{
				Json.JsonlOutput.ClaimStdout();
			}

			// Need to register the code pages provider for code that parses
			// and later needs ISO-8859-2
			System.Text.Encoding.RegisterProvider(
				System.Text.CodePagesEncodingProvider.Instance);
			// Test that it loads
			_ = System.Text.Encoding.GetEncoding("ISO-8859-2");

			RegisterCheckups();

			return CreateCommandApp().RunAsync(BuildFinalArgs(args));
		}

		static void RegisterCheckups()
		{
			CheckupManager.RegisterCheckups(
				new OpenJdkCheckup(),
				new AndroidEmulatorCheckup(),
				new VisualStudioWindowsCheckup(),
				new VSWinWorkloadsCheckup(),
				new HttpsDevCertCheckup(),
				new AndroidSdkPackagesCheckup(),
				new XCodeCheckup(),
				new DotNetCheckup()
			);

			CheckupManager.RegisterCheckupContributors(
				new DotNetSdkCheckupContributor(),
				new VSRestartCheckupContributor()
			);

			CheckupManager.RegisterCheckups(
				new PSExecutionPolicyCheckup(),
				new WindowsPythonInstallationCheckup(),
				new WindowsLongPathCheckup(),
				new GitCheckup(),
				new LinuxNinjaPresenceCheckup(),
				new HyperVCheckup(),
				new DotNetNewUnoTemplatesCheckup(),
				new UnoSdkCheckup(),
				new EdgeWebView2Checkup(),
				new DotNetRootsCheckup(),
				new DotNetTargetingPackAlignmentCheckup()
			);
		}

		/// <summary>
		/// Builds the command app. Separated from <see cref="Main"/> so tests can construct it
		/// after claiming stdout and assert that the command infrastructure's own failures
		/// (unparseable arguments, a missing manifest) stay off the event stream — the app
		/// captures whatever stdout is current at construction, which is what makes the order
		/// in <see cref="Main"/> load-bearing.
		/// </summary>
		internal static CommandApp CreateCommandApp()
		{
			var app = new CommandApp();

			app.Configure(config =>
			{
				var version = ToolInfo.CurrentVersion;
				var buildDateMeta = typeof(ToolInfo).Assembly
					.GetCustomAttributes<AssemblyMetadataAttribute>()
					.First(a => a.Key == "BuildDate");
				var buildDate = buildDateMeta.Value;
				var versionText = $"Uno.Check Version {version} (built {buildDate})";

				// Pin the app to the console that is current now rather than leaving it to
				// Spectre's ambient default: with stdout already claimed, its own parse and
				// startup errors then render to stderr instead of onto the event stream.
				config.ConfigureConsole(Spectre.Console.AnsiConsole.Console);

				config.SetApplicationName(ToolInfo.ToolCommand);
				config.SetApplicationVersion(versionText);
				config.AddCommand<CheckCommand>("check");
				config.AddCommand<ListCheckupCommand>("list");
				config.AddCommand<ConfigCommand>("config");
			});

			return app;
		}

		/// <summary>
		/// "check" is the implied command, so a bare option list still routes somewhere.
		/// </summary>
		internal static List<string> BuildFinalArgs(string[] args)
		{
			var finalArgs = new List<string>();

			var firstArg = args?.FirstOrDefault()?.Trim()?.ToLowerInvariant() ?? string.Empty;
			var isGlobalOption = firstArg is "-h" or "--help" or "--version";
			var isExplicitCommand = firstArg is "check" or "list" or "config";

			if (!isGlobalOption && !isExplicitCommand)
				finalArgs.Add("check");

			if (args?.Any() ?? false)
				finalArgs.AddRange(args);

			return finalArgs;
		}
	}
}
