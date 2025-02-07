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
	}
}
