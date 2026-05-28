using System.Threading.Tasks;

using DotNetCheck.Models;
using DotNetCheck.Solutions;

namespace DotNetCheck.Checkups
{
	internal class WinAppCliCheckup : Checkup
	{
		private const string SuggestionMessage = "WinApp CLI is not installed. To learn more visit https://devblogs.microsoft.com/ifdef-windows/introducing-dotnet-new-templates-for-winui/";

		public override string Id => "winappcli";

		public override string Title => "WinApp CLI";

		public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;

		public override bool ShouldExamine(SharedState history)
			=> Manifest?.Check?.VSWin != null;

		public override Task<DiagnosticResult> Examine(SharedState history)
		{
			var result = new ShellProcessRunner(new("winapp", "--version") { Verbose = true }).WaitForExit();

			if (result.Success)
			{
				var version = string.Join(" ", result.StandardOutput).Trim();
				ReportStatus(
					string.IsNullOrEmpty(version)
						? "WinApp CLI is installed."
						: $"WinApp CLI {version} is installed.",
					Status.Ok);
				return Task.FromResult(DiagnosticResult.Ok(this));
			}

			return Task.FromResult(new DiagnosticResult(
				Status.Error,
				this,
				"WinApp CLI is not installed.",
				new Suggestion(SuggestionMessage, new WinAppCliInstallSolution())));
		}
	}
}
