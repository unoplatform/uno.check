using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

			// Need to register the code pages provider for code that parses
			// and later needs ISO-8859-2
			System.Text.Encoding.RegisterProvider(
				System.Text.CodePagesEncodingProvider.Instance);
			// Test that it loads
			_ = System.Text.Encoding.GetEncoding("ISO-8859-2");

			CheckupManager.RegisterCheckups(
				new OpenJdkCheckup(),
				new AndroidEmulatorCheckup(),
				new VisualStudioWindowsCheckup(),
				new VSWinWorkloadsCheckup(),
				new AndroidSdkPackagesCheckup(),
				new XCodeCheckup(),
				new DotNetCheckup()
			);

			CheckupManager.RegisterCheckupContributors(
				new DotNetSdkCheckupContributor());

			CheckupManager.RegisterCheckups(
				new PSExecutionPolicyCheckup(),
				new WindowsPythonInstallationCheckup(),
				new WindowsLongPathCheckup(),
				new GitCheckup(),
				new LinuxNinjaPresenceCheckup(),
				new HyperVCheckup(),
				new DotNetNewUnoTemplatesCheckup(),
				new UnoSdkCheckup(),
				new EdgeWebView2Checkup()
			);

			var app = new CommandApp();

			app.Configure(config =>
			{
				var version = ToolInfo.CurrentVersion;
				var buildDateMeta = typeof(ToolInfo).Assembly
					.GetCustomAttributes<AssemblyMetadataAttribute>()
					.First(a => a.Key == "BuildDate");
				var buildDate = buildDateMeta.Value;
				var versionText = $"Uno.Check Version {version} (built {buildDate})";

				config.SetApplicationName(ToolInfo.ToolCommand);
				config.SetApplicationVersion(versionText);
				config.AddCommand<CheckCommand>("check");
				config.AddCommand<ListCheckupCommand>("list");
				config.AddCommand<ConfigCommand>("config");
			});

			var finalArgs = new List<string>();

			var firstArg = args?.FirstOrDefault()?.Trim()?.ToLowerInvariant() ?? string.Empty;
			var isGlobalOption = firstArg is "-h" or "--help" or "-v" or "--version";
			if (!isGlobalOption && firstArg != "list" && firstArg != "config" && firstArg != "acquirepackages")
				finalArgs.Add("check");

			if (args?.Any() ?? false)
				finalArgs.AddRange(args);

			return app.RunAsync(finalArgs);
		}
	}
}
