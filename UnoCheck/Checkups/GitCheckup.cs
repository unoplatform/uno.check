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
		internal static bool RunFromKnownInstall(string command, System.Func<string, bool> run)
			=> run(command) || (command != ToolPaths.GitName && run(ToolPaths.GitName));

		public override async Task<DiagnosticResult> Examine(SharedState state)
		{
			var installed = RunFromKnownInstall(
				ToolPaths.Git(),
				command => new ShellProcessRunner(new(command, "--version") { Verbose = true }).WaitForExit().Success);

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