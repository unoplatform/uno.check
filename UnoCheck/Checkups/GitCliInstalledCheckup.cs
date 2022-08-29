#nullable enable

using DotNetCheck.Models;
using DotNetCheck.Solutions;
using System.Threading.Tasks;

namespace DotNetCheck.Checkups
{
	public class GitCliInstalledCheckup : Checkup
	{
		public override bool IsPlatformSupported(Platform platform)
			=> platform == Platform.Linux || platform == Platform.Windows || platform == Platform.OSX;

		public override string Id => "gitcli";

		public override string Title => "Git Cli";

		public override async Task<DiagnosticResult> Examine(SharedState history)
		{
			//just for macOS: If you don’t have it installed already, it will prompt you to install it.
			var shellHasGitRunnerResult = ShellProcessRunner.Run("git", "--version");

			if (Util.IsWindows && shellHasGitRunnerResult.ExitCode != 0)
			{
				return await Task.FromResult(
					new DiagnosticResult(
						Status.Error,
						this,
						new Suggestion(InstallMessage, new GitCliSolution())));
			}

			if (Util.IsLinux)
			{
				var shellRunnerAboutLinuxResult = ShellProcessRunner.Run("lsb_release", "-a");

				var linuxRelease = shellRunnerAboutLinuxResult.GetOutput().Trim();

				var isDebianBased = linuxRelease.Contains(Ubuntu) || linuxRelease.Contains(Debian);

				if (shellHasGitRunnerResult.ExitCode != 0 && isDebianBased)
				{
					return await Task.FromResult(new DiagnosticResult(
					Status.Error,
					this,
					new Suggestion(InstallMessage,
					new LinuxGitCliSolution())));
				}

				if (shellHasGitRunnerResult.ExitCode != 0 && !isDebianBased)
				{
					return await Task.FromResult(new DiagnosticResult(
					Status.Error,
					this,
					new Suggestion(InstallMessage,
					new LinuxOtherDistGitCliSolution())));
				}
			}

			ReportStatus($"{shellHasGitRunnerResult.GetOutput().Trim()}", Status.Ok);

			return await Task.FromResult(DiagnosticResult.Ok(this));
		}

		private const string Ubuntu = "Ubuntu";
		private const string Debian = "Debian";
		private const string InstallMessage = "Install Git CLI";
	}
}