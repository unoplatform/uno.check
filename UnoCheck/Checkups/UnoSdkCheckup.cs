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

				if (unoSdkVersion is null)
				{
					var latest = await NuGetHelper.GetLatestPackageVersionAsync("Uno.Sdk", ToolInfo.IncludePrereleaseUnoPackages(Manifest));
					if (latest is null)
					{
						return new DiagnosticResult(
							Status.Warning,
							this,
							"Could not determine the latest Uno.Sdk version from the configured NuGet sources.");
					}

					unoSdkVersion = latest.ToString();
				}
			}

			var packagesPath = NuGetSourceResolver.Current?.GlobalPackagesFolder
				?? SettingsUtility.GetGlobalPackagesFolder(Settings.LoadDefaultSettings(root: null));

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
