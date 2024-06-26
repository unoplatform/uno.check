using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using DotNetCheck.DotNet;
using DotNetCheck.Models;
using DotNetCheck.Solutions;
using NuGet.Configuration;

namespace DotNetCheck.Checkups
{
	internal class UnoSdkCheckup : Checkup
	{
		public override IEnumerable<CheckupDependency> DeclareDependencies(IEnumerable<string> checkupIds) => new[] { new CheckupDependency("dotnet") };

		public override string Id => "unosdk";

		public override string Title => "Uno SDK";

		public override async Task<DiagnosticResult> Examine(SharedState state)
		{
			state.TryGetState("unosdk", "unoSdkLatestVersion", out string unoSdkLatestVersion);

			unoSdkLatestVersion ??= (await NuGetHelper.GetLatestPackageVersionAsync("Uno.Sdk", ToolInfo.CurrentVersion.IsPrerelease)).ToString();

			var defaultSettings = Settings.LoadDefaultSettings(root: null);

			var packagesPath = SettingsUtility.GetGlobalPackagesFolder(defaultSettings);

			if (Directory.Exists(Path.Combine(packagesPath, "uno.sdk", unoSdkLatestVersion)))
			{
				return DiagnosticResult.Ok(this);
			}
			else
			{
				state.ContributeState(this, "unoSdkLatestVersion", unoSdkLatestVersion);

				return new DiagnosticResult(
					Status.Error,
					this,
					$"Uno.Sdk {unoSdkLatestVersion} is not installed.",
					new Suggestion($"Install Uno.Sdk {unoSdkLatestVersion}", new UnoSdkSolution()));
			}
		}
	}
}
