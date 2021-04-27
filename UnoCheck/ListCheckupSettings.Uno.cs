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
	}
}