﻿#nullable enable
using Spectre.Console.Cli;
using System.ComponentModel;

namespace DotNetCheck
{
	public partial class CheckSettings : CommandSettings, IManifestChannelSettings
	{
		[CommandOption("-m|--manifest <FILE_OR_URL>")]
		public string Manifest { get; set; }

		[CommandOption("-f|--fix")]
		public bool Fix { get; set; }

		[CommandOption("-n|--non-interactive")]
		public bool NonInteractive { get; set; }

		[CommandOption("-s|--skip <CHECKUP_ID>")]
		public string[]? Skip { get; set; }

		[CommandOption("--pre|--preview|-d|--dev")]
		public bool Preview { get; set; }

        [CommandOption("--pre-major|--preview-major")]
        public bool PreviewMajor { get; set; }

        [CommandOption("--main")]
		public bool Main { get; set; }

		[CommandOption("--dotnet <SDK_ROOT>")]
		public string DotNetSdkRoot { get; set; }

		[CommandOption("--force-dotnet")]
		public bool ForceDotNet { get; set; }

		[CommandOption("-v|--verbose")]
		public bool Verbose { get; set; }

		[CommandOption("--ci")]
		public bool CI { get; set; }

		[CommandOption("-l|--logfile <PATH>")]
		public string LogFile { get; set; }
	}
}
