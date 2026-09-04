#nullable enable

using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotNetCheck
{
	partial class ListCheckupSettings
	{
		[CommandOption("--target <TARGET_PLATFORM_ID>")]
		[Description("List checks for a specific target platform. Use the --target option multiple times for multiple platforms, or omit it to list checks for all supported platforms.")]
		public string[]? TargetPlatforms { get; set; }

		[CommandOption("--json")]
		[Description("Emit the checkup catalog as a single JSON line on stdout, for hosts building checkup-selection UIs. Implies --non-interactive.")]
		public bool Json { get; set; }
	}
}