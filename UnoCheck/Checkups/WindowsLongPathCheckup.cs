#nullable enable

using DotNetCheck.Models;
using DotNetCheck.Solutions;
using Microsoft.Win32;
using System.Threading.Tasks;

namespace DotNetCheck.Checkups
{
	public class WindowsLongPathCheckup : Checkup
	{
		internal const string FileSystemKeyPath = @"SYSTEM\CurrentControlSet\Control\FileSystem";

		private const string LongPathsEnabledKey = "LongPathsEnabled";

		public override string Id => "windowslongpath";

		public override string Title => "Windows Long Path";

		public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;

		public override Task<DiagnosticResult> Examine(SharedState history)
		{
			// Read-only. Asking for a writable handle here threw "Requested registry access is
			// not allowed" for every unelevated caller, which surfaced as a hard error on a
			// machine that was configured correctly — and one with no fix attached, because the
			// throw happened before any suggestion could be built. Only the fix needs to write.
			using var fileSystemKey = Registry.LocalMachine.OpenSubKey(FileSystemKeyPath);

			if (fileSystemKey is null)
			{
				return Task.FromResult(new DiagnosticResult(
					Status.Warning,
					this,
					$@"Could not read HKEY_LOCAL_MACHINE\{FileSystemKeyPath}, so long path support could not be determined."));
			}

			if (int.TryParse(fileSystemKey.GetValue(LongPathsEnabledKey)?.ToString(), out var value) && value is 0)
			{
				return Task.FromResult(new DiagnosticResult(
					Status.Error,
					this,
					new Suggestion(
						"Enable long paths support on Windows",
						new LongPathsNotEnabledSolution(FileSystemKeyPath, LongPathsEnabledKey))));
			}

			ReportStatus("Long paths are enabled on Windows!", Status.Ok);

			return Task.FromResult(DiagnosticResult.Ok(this));
		}
	}
}
