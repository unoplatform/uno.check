#nullable enable

using DotNetCheck.Models;
using DotNetCheck.Solutions;
using System.Threading.Tasks;

namespace DotNetCheck.Checkups
{
	public class LinuxNinjaPresenceCheckup : Checkup
	{
		public override bool IsPlatformSupported(Platform platform)
			=> platform == Platform.Linux;

		public override string Id => "linuxninja";

		public override string Title => "Linux Ninja Build";

		public override TargetPlatform GetApplicableTargets(Manifest.Manifest manifest) => TargetPlatform.SkiaGtk;

		public override async Task<DiagnosticResult> Examine(SharedState history)
		{
			var shellHasNinjaRunnerResult = ShellProcessRunner.Run("ninja", "--version");
			var shellRunnerAptResult = ShellProcessRunner.Run("apt", "--version");

			var ninjaVersionExitCode = shellHasNinjaRunnerResult.ExitCode;
			var output = shellHasNinjaRunnerResult.GetOutput().Trim();

			var shellRunnerAboutLinuxResult = ShellProcessRunner.Run("lsb_release", "-a");

			var linuxRelease = shellRunnerAboutLinuxResult.GetOutput().Trim();

			var hasNinja = ninjaVersionExitCode == 0;
			var hasApt = shellRunnerAptResult.ExitCode == 0;
			var isDebianBased = linuxRelease.Contains(Ubuntu) || linuxRelease.Contains(Debian);

			if (!hasNinja && (isDebianBased || hasApt))
			{
				return await Task.FromResult(
					new DiagnosticResult(
						Status.Error,
						this,
						new Suggestion(InstallMessage, new LinuxNinjaSolution())));
			}

			ReportStatus($"Ninja Build Version: {output}", Status.Ok);

			return await Task.FromResult(DiagnosticResult.Ok(this));
		}

		private const string Ubuntu = "Ubuntu";
		private const string Debian = "Debian";
		private const string InstallMessage = "Install Ninja Build";
	}
}