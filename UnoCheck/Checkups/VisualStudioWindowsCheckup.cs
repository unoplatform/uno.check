using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DotNetCheck.Models;
using NuGet.Versioning;

namespace DotNetCheck.Checkups
{
	public class VisualStudioWindowsCheckup : Checkup
	{
		private static readonly Task<IEnumerable<VisualStudioInfo>> EmptyResult = 
			Task.FromResult(Enumerable.Empty<VisualStudioInfo>());

		// Read once per run: both Visual Studio checks ask, and a sandboxed host asks the user for each read.
		internal static readonly Lazy<bool> SetupStateReadRefused = SetupStateProbe();

		// vswhere's plain listing, once per run: both checks need it, and in a sandbox each run of vswhere reads the setup folder.
		static readonly Lazy<IReadOnlyList<VisualStudioInfo>> Installs = new(() => GetWindowsInfo().GetAwaiter().GetResult().ToList());

		public override bool IsPlatformSupported(Platform platform)
			=> platform == Platform.Windows;

		public NuGetVersion MinimumVersion
			=> Extensions.ParseVersion(Manifest?.Check?.VSWin?.MinimumVersion, new NuGetVersion("16.11.0"));

		public NuGetVersion ExactVersion
			=> Extensions.ParseVersion(Manifest?.Check?.VSWin?.ExactVersion);

		public override string Id => "vswin";

		public override string Title => $"Visual Studio {MinimumVersion.ThisOrExact(ExactVersion)}";
		
		public override bool ShouldExamine(SharedState history)
			=> Manifest?.Check?.VSWin != null;

		public override async Task<DiagnosticResult> Examine(SharedState history)
		{
			var vsinfo = Installs.Value;

			// Defensive null check (should not occur with current implementation, but guards against future changes)
			if (vsinfo == null)
			{
				ReportStatus("Visual Studio is not installed", Status.Warning);
				return new DiagnosticResult(Status.Warning, this);
			}

			foreach (var vi in vsinfo)
			{
				if (vi.Version.IsCompatible(MinimumVersion, ExactVersion))
					ReportStatus($"{vi.Version} - {vi.Path}", Status.Ok);
				else
					ReportStatus($"{vi.Version}", null);
			}

			if (vsinfo.Any(vs => vs.Version.IsCompatible(MinimumVersion, ExactVersion)))
				return DiagnosticResult.Ok(this);

			if (vsinfo.Any())
			{
				ReportStatus($"Missing Visual Studio >= {MinimumVersion.ThisOrExact(ExactVersion)}", Status.Error);

				return new DiagnosticResult(Status.Error, this);
			}
			else
			{
				return NothingFoundResult(IsSetupStateReadRefused());
			}
		}

		/// <summary>Where Visual Studio records its installs, which vswhere reads.</summary>
		internal static string SetupStateFolder
			=> Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "VisualStudio", "Setup");

		// vswhere answers "no installs" when it is refused that folder, so an empty answer is checked against it.
		internal static bool IsSetupStateReadRefused()
			=> IsSetupStateReadRefused(AppContainer.IsCurrentProcess, () => VsWhereFoundNoInstall(VsWherePath(), () => Installs.Value), SetupStateReadRefused);

		// In an AppContainer vswhere's plain listing decides, without reading the folder: vswhere was just asked for it,
		// and the probe would ask the user a second time.
		internal static bool IsSetupStateReadRefused(bool inAppContainer, Func<bool> vsWhereFoundNoInstall, Lazy<bool> probe)
			=> inAppContainer ? vsWhereFoundNoInstall() : probe.Value;

		// A missing vswhere read nothing, so its empty answer can't mean the folder was refused.
		internal static bool VsWhereFoundNoInstall(string vsWherePath, Func<IEnumerable<VisualStudioInfo>> installs)
			=> vsWherePath is not null && !installs().Any();

		internal static Lazy<bool> SetupStateProbe(Func<string, IEnumerable<string>> enumerate = null)
			=> new(() => AccessRefused.IsReadRefused(SetupStateFolder, enumerate));

		internal DiagnosticResult NothingFoundResult(bool setupStateReadRefused)
		{
			if (setupStateReadRefused)
				return AccessRefused.FolderResult(this, SetupStateFolder, "installed Visual Studio versions can't be listed");

			// VS is not installed, but another IDE might be, so don't raise an error
			ReportStatus("Visual Studio is not installed", Status.Warning);
			return new DiagnosticResult(Status.Warning, this);
		}

		static string VsWherePath()
			=> new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
				.Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Microsoft Visual Studio", "Installer", "vswhere.exe"))
				.FirstOrDefault(File.Exists);

		public static Task<IEnumerable<VisualStudioInfo>> GetWindowsInfo(string requires = "")
		{
			var items = new List<VisualStudioInfo>();

			var path = VsWherePath();

			if (path is null)
				return EmptyResult;

			var r = ShellProcessRunner.Run(path,
				$"-all -requires Microsoft.Component.MSBuild {requires} -format json -prerelease");

			var str = r.GetOutput();

			var json = JsonDocument.Parse(str);

			foreach (var vsjson in json.RootElement.EnumerateArray())
			{
				if (!vsjson.TryGetProperty("catalog", out var vsCat) || !vsCat.TryGetProperty("productSemanticVersion", out var vsSemVer))
					continue;

				if (!NuGetVersion.TryParse(vsSemVer.GetString(), out var semVer))
					continue;

				if (!vsjson.TryGetProperty("installationPath", out var installPath))
					continue;

				items.Add(new VisualStudioInfo
				{
					Version = semVer,
					Path = installPath.GetString()
				});
			}

			return Task.FromResult<IEnumerable<VisualStudioInfo>>(items);
		}
	}

	public struct VisualStudioInfo
	{
		public string Path { get; set; }

		public NuGetVersion Version { get; set; }
	}
}
