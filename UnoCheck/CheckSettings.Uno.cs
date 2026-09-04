#nullable enable

using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace DotNetCheck
{
	partial class CheckSettings
	{
		[CommandOption("--target <TARGET_PLATFORM_ID>")]
		[Description(
@"Run checks for a specific target platform. Use the --target option multiple times to run checks for multiple platforms, or omit it to run checks for all supported platforms.
Targets: webassembly ios android macos linux windows"
			)]
		public string[]? TargetPlatforms { get; set; }
        
        [CommandOption("--tfm <TARGET_FRAMEWORK>")]
        [Description(
            @"Run checks for a specific TFM. Use the --framework option multiple times to run checks for multiple TFM's, or omit it to run checks for all supported platforms.")]
        public string[]? Frameworks { get; set; }
        
        [CommandOption("--ide <IDE_NAME>")]
        [Description(
            @"This parameter skips some checks based on the IDE which is used to run the Uno.Check.")]
        public string? Ide { get; set; }
        
        [CommandOption("--unoSdkVersion <UNO_SDK_VERSION>")]
        [Description(
	        @"Uno SDK Checkup will validate if provided Uno SDK version is installed.")]
        public string? UnoSdkVersion { get; set; }

        [CommandOption("--json")]
        [Description(
            @"Emit JSONL progress events and a final JSON report on stdout for GUI/CI/agent consumers. Human-readable output moves to stderr. Implies --non-interactive.")]
        public bool Json { get; set; }

        [CommandOption("--json-file <PATH>")]
        [Description(
            @"Append the same JSONL events to a file. Intended for elevated child processes whose stdout cannot be redirected across the elevation boundary.")]
        public string? JsonFile { get; set; }

        [CommandOption("--only <CHECKUP_ID>")]
        [Description(
            @"Run only the specified checkup(s), plus any checkups they require. Ids match exactly (case-insensitive); an unknown id fails the run. Use the --only option multiple times for multiple checkups. See the list command for checkup ids.")]
        public string[]? Only { get; set; }

        [CommandOption("--correlation-id <ID>")]
        [Description(
            @"Correlation id stamped on structured-output events. Hosts pass their own id when launching a child process (e.g. an elevated fix) so both processes report as one logical run. Defaults to a new id per run.")]
        public string? CorrelationId { get; set; }

        [CommandOption("--allow-elevation-prompt")]
        [Description(
            @"Allow a structured macOS or Linux fix run to display the system authorization dialog (macOS administrator prompt / Linux polkit). Ignored in CI.")]
        public bool AllowElevationPrompt { get; set; }
	}
}
