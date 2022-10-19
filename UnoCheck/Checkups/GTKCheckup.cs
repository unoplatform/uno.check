#nullable enable

using DotNetCheck.Models;
using DotNetCheck.RuntimePlatform.OperatingSystem;
using DotNetCheck.Solutions;
using System.IO;
using System.Threading.Tasks;

namespace DotNetCheck.Checkups
{
	public class GTK3Checkup : Checkup
	{
		public override bool IsPlatformSupported(Platform platform)
			=> platform == Platform.Linux || platform == Platform.OSX || platform == Platform.Windows;

		public override string Id => "gtk3";

		public override string Title => "GTK3+";

		public override TargetPlatform GetApplicableTargets(Manifest.Manifest manifest) => TargetPlatform.SkiaGtk;

		public override Task<DiagnosticResult> Examine(SharedState history)
		{
			if (Util.IsMac)
			{
				if (!File.Exists("/usr/local/lib/libgdk-3.0.dylib") && !File.Exists("/opt/homebrew/lib/libgdk-3.0.dylib"))
				{
					return Task.FromResult(new DiagnosticResult(
						Status.Error,
						this,
						new Suggestion($"Install Gtk+3 with \"brew install gtk+3 gnome-icon-theme\"")));
				}
			}
			else if (Util.IsLinux)
			{
				var r = ShellProcessRunner.Run("whereis", "libgdk-3.so.0");

				if (!r.GetOutput().Trim().Contains('/'))
				{
					return Task.FromResult(new DiagnosticResult(
						Status.Error,
						this,
						new Suggestion($"Install Gtk+3 following this instructions: https://platform.uno/docs/articles/get-started-with-linux.html#setting-up-for-linux")));
				}
			}
			else if (Util.IsWindows)
			{
				var url = Manifest?.Check?.Gtk?.Urls.Get(Manifest.Check.Gtk.Version, Platform.Windows);

				var runtimePlatform = new WindowsOperatingSystem();

				if (!runtimePlatform.IsProgramInstalled(Id.ToUpper()))
				{
					return Task.FromResult(
					new DiagnosticResult(
						Status.Error,
						this,
						new Suggestion("Install GTK+3 on Windows.", new GTK3NotInstalledOnWindowsSolution(url, Title))));
				}
				else
				{
					ReportStatus($"GTK+3 is installed!", Status.Ok);
				}
			}

			return Task.FromResult(DiagnosticResult.Ok(this));
		}
	}
}