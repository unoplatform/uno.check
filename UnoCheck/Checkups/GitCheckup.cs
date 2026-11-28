using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using DotNetCheck.Models;
using DotNetCheck.Solutions;

namespace DotNetCheck.Checkups
{
	internal class GitCheckup : Checkup
	{
		private const string ManualInstallMessage = "Git cannot be installed automatically. To learn more visit https://git-scm.com/book/en/v2/Getting-Started-Installing-Git";

		public override string Id => "git";

		public override string Title => "Git";

		// A known install that doesn't run, for example a leftover folder, must not hide a Git that does.
		internal static bool RunFromKnownInstall(string command, Func<string, bool> run)
			=> run(command) || (command != ToolPaths.GitName && run(ToolPaths.GitName));

		// Git reads its system and global config files at start, even for --version; a sandboxed host asks the user
		// for the global one. Git 2.32 and later honour both variables; an older Git reads the files as before.
		internal static ShellProcessRunnerOptions VersionCommand(string command)
			=> new(command, "--version")
			{
				Verbose = true,
				EnvironmentVariables = new Dictionary<string, string>
				{
					["GIT_CONFIG_NOSYSTEM"] = "1",
					["GIT_CONFIG_GLOBAL"] = Util.IsWindows ? "NUL" : "/dev/null",
				},
			};

		public override async Task<DiagnosticResult> Examine(SharedState state)
		{
			var installed = RunFromKnownInstall(
				ToolPaths.Git(),
				command => new ShellProcessRunner(VersionCommand(command)).WaitForExit().Success);

			if (installed)
			{
				return DiagnosticResult.Ok(this);
			}
			else
			{
				if (Util.IsWindows)
				{
					if (!Util.CI && (await VisualStudioWindowsCheckup.GetWindowsInfo()).Any())
					{
						return new DiagnosticResult(Status.Error, this, "Git is not installed.", new Suggestion("Install Git with VS installer", new GitSolution()));
					}

					return new DiagnosticResult(Status.Error, this, ManualInstallMessage);
				}
				else if (Util.IsMac)
				{
					return new DiagnosticResult(Status.Error, this, ManualInstallMessage);
				}
				else
				{
					return new DiagnosticResult(Status.Error, this, "Git is not installed.", new Suggestion("Install Git", new LinuxGitSolution()));
				}
			}
		}
	}
}