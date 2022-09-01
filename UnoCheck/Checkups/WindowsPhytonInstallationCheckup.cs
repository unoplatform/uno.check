#nullable enable

using DotNetCheck.Models;
using DotNetCheck.Solutions;
using System.Threading.Tasks;

namespace DotNetCheck.Checkups
{
	public class WindowsPhytonInstallationCheckup : Checkup
	{
		public override string Id => "windowsphytonInstallation";

		public override string Title => "Windows Phyton Installation Checkup";

		public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;

		public override async Task<DiagnosticResult> Examine(SharedState history)
		{
			if (!this.PhytonIsPresent())
			{
				return await Task.FromResult(
					this.PhytonIsNotPresentDialog("In order to build WebAssembly apps using AOT, you will need to install Python from Windows Store, or manually through Python's official site"));
			}
			else
			{
				ReportStatus($"Python is installed.", Status.Ok);
			}

			return await Task.FromResult(DiagnosticResult.Ok(this));
		}

		private DiagnosticResult PhytonIsNotPresentDialog(string message)
		{
			return new DiagnosticResult(
				Status.Error,
				this,
				new Suggestion(message,
				new PytonIsInstalledSolution()));
		}

		private bool PhytonIsPresent()
		{
			var r = ShellProcessRunner.Run("python", "--version");

			return r.ExitCode == 0;
		}

		private const string InstallDirKey = "InstallDir";
	}
}