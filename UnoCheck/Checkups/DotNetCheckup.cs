using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DotNetCheck.DotNet;
using DotNetCheck.Models;
using DotNetCheck.Solutions;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;

namespace DotNetCheck.Checkups
{
	public class DotNetCheckup : Checkup
	{
		public IEnumerable<Manifest.DotNetSdk> RequiredSdks
			=> Manifest?.Check?.DotNet?.Sdks;

		public override string Id => "dotnet";

		public override string Title => $".NET SDK";

		string SdkListToString()
			=> (RequiredSdks?.Any() ?? false) ? "(" + string.Join(", ", RequiredSdks.Select(s => s.Version)) + ")" : string.Empty;

		public override bool ShouldExamine(SharedState history)
			=> RequiredSdks?.Any() ?? false;

		public override async Task<DiagnosticResult> Examine(SharedState history)
		{
			var dn = new DotNetSdk(history);

			var missingDiagnosis = new DiagnosticResult(Status.Error, this, new Suggestion(".NET SDK not installed"));

			if (!dn.Exists)
				return missingDiagnosis;

			var sdks = (await dn.GetSdks()).ToList();

			var missingSdks = new List<Manifest.DotNetSdk>();
			var sentinelFiles = new List<string>();

			if (RequiredSdks?.Any() ?? false)
			{
				foreach (var rs in RequiredSdks)
				{
					var rsVersion = NuGetVersion.Parse(rs.Version);

					if (!sdks.Any(s => (rs.RequireExact && s.Version == rsVersion) || (!rs.RequireExact && s.Version >= rsVersion)))
						missingSdks.Add(rs);
				}
			}

			foreach (var sdk in sdks)
			{
				// See if the sdk is one of the required sdk's
				var requiredSdk = RequiredSdks.FirstOrDefault(rs => sdk.Version == NuGetVersion.Parse(rs.Version));

				if (requiredSdk != null)
				{
					if (requiredSdk.EnableWorkloadResolver)
					{
						var sentinelPath = Path.Combine(sdk.Directory.FullName, "EnableWorkloadResolver.sentinel");
						sentinelFiles.Add(sentinelPath);
					}

					ReportStatus($"{sdk.Version} - {sdk.Directory}", Status.Ok);
				}
				else
					ReportStatus($"{sdk.Version} - {sdk.Directory}", null);
			}

			var selection = await DotNetSdkSelector.ResolveAsync(history, dn, sdks, RequiredSdks.ToList());

			history.ContributeState(this, StateKey.DotNetSdkSelection, selection);

			if (selection.Error is not null)
				return new DiagnosticResult(Status.Error, this, selection.Error);

			// The one SDK the workloads are checked and installed with
			if (selection.Sdk is { } bestSdk)
			{
				ReportStatus(DescribeSelection(selection), null);

				history.SetEnvironmentVariable("DOTNET_SDK", bestSdk.Directory.FullName);
				history.SetEnvironmentVariable("DOTNET_SDK_VERSION", bestSdk.Directory.Name);
			}

			// Add sentinel files that should be considered
			if (sentinelFiles.Any())
				history.ContributeState(this, "sentinel_files", sentinelFiles.ToArray());

			if (missingSdks.Any())
			{
				var str = SdkListToString();

				var remedies = new List<Solution>();

				if (Util.CI || Util.IsLinux)
				{
					remedies.AddRange(missingSdks
						.Select(ms => new DotNetSdkScriptInstallSolution(ms.Version, history)));
				}
				else
				{
					remedies.AddRange(missingSdks
						.Where(ms => !ms.Url.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
						.Select(ms => new BootsSolution(ms.Url, ".NET SDK " + ms.Version) as Solution));

					remedies.AddRange(missingSdks
						.Where(ms => ms.Url.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
						.Select(ms => new MsInstallerSolution(ms.Url, ".NET SDK " + ms.Version)));
				}

				return new DiagnosticResult(Status.Error, this, $".NET SDK {str} not installed.",
							new Suggestion($"Download .NET SDK {str}",
							remedies.ToArray()));
			}

			return selection.Warnings.Count > 0
				? new DiagnosticResult(Status.Warning, this, string.Join(" ", selection.Warnings))
				: new DiagnosticResult(Status.Ok, this);
		}

		internal static string DescribeSelection(DotNetSdkSelection selection)
		{
			var origin = selection.Source switch
			{
				DotNetSdkSelectionSource.Requested => " as requested with --dotnet-sdk-version",
				DotNetSdkSelectionSource.GlobalJson => $" as selected by {selection.GlobalJsonPath}",
				_ => string.Empty,
			};

			return $"Using .NET SDK {selection.Sdk.Directory.Name} to check and install workloads{origin}";
		}
	}
}
