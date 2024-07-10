#nullable enable

using DotNetCheck.Models;
using DotNetCheck.Solutions;
using Microsoft.Win32;
using System.Threading.Tasks;

namespace DotNetCheck.Checkups
{
	public class WindowsLongPathCheckup : Checkup
	{
		public override string Id => "windowslongpath";

		public override string Title => "Windows Long Path";

		public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;

		public override async Task<DiagnosticResult> Examine(SharedState history)
		{
			RegistryKey fileSystemKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem", true);

			if (int.TryParse(fileSystemKey.GetValue(LongPathsEnabledKey)?.ToString(), out var value) && value is 0)
			{
				return await Task.FromResult(new DiagnosticResult(
				Status.Error,
				this,
				new Suggestion("Enable long paths support on Windows",
				new LongPathsNotEnabledSolution(fileSystemKey, LongPathsEnabledKey))));
			}
			else
			{
				ReportStatus($"Long paths are enabled on Windows!", Status.Ok);
			}

			return await Task.FromResult(DiagnosticResult.Ok(this));
		}

		private const string LongPathsEnabledKey = "LongPathsEnabled";
	}
}
