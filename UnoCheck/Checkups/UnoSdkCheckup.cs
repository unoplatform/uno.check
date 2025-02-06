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
			string unoSdkVersion;
			if (state.TryGetEnvironmentVariable("UnoSdkVersion", out var version))
			{
				unoSdkVersion = version;
			}
			else
			{
				state.TryGetState("unosdk", "unoSdkLatestVersion", out unoSdkVersion);

				unoSdkVersion ??= (await NuGetHelper.GetLatestPackageVersionAsync("Uno.Sdk", ToolInfo.CurrentVersion.IsPrerelease)).ToString();	
			}

			var defaultSettings = Settings.LoadDefaultSettings(root: null);

			var packagesPath = SettingsUtility.GetGlobalPackagesFolder(defaultSettings);

			if (Directory.Exists(Path.Combine(packagesPath, "uno.sdk", unoSdkVersion)))
			{
				return DiagnosticResult.Ok(this);
			}

			state.ContributeState(this, "unoSdkLatestVersion", unoSdkVersion);

			return new DiagnosticResult(
				Status.Error,
				this,
				$"Uno.Sdk {unoSdkVersion} is not installed.",
				new Suggestion($"Install Uno.Sdk {unoSdkVersion}", new UnoSdkSolution()));
		}
	}
}
