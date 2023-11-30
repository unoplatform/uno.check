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
			var output = shellHasNinjaRunnerResult.GetOutput().Trim();

			var hasNinja = shellHasNinjaRunnerResult.Success;
			if (hasNinja)
			{
				ReportStatus($"Ninja Build Version: {output}", Status.Ok);

				return await Task.FromResult(DiagnosticResult.Ok(this));
			};

			var foundPackage = await LinuxPackageManagerWrapper.SearchForPackage(LinuxNinjaSolution.NinjaPackageNamesWithWrappers, true);
			if (foundPackage)
			{
				return await Task.FromResult(
					new DiagnosticResult(
						Status.Error,
						this,
						new Suggestion(InstallMessage, new LinuxNinjaSolution())
					));
			}
			
			return await Task.FromResult(
				new DiagnosticResult(
					Status.Error,
					this,
					new Suggestion(InstallMessage, "Ninja-build is missing, follow the installation instructions here: https://github.com/ninja-build/ninja/wiki/Pre-built-Ninja-packages")));
		}

		private const string InstallMessage = "Install Ninja Build";
	}
}