using DotNetCheck.Models;
using Polly;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class BootsSolution : Solution
	{
		const string MacInstallerPath = "/usr/sbin/installer";
		const int DownloadRetries = 3;

		public BootsSolution(Uri url, string title)
		{
			Url = url;
			Title = title;
		}

		public Uri Url { get; set; }
		public string Title { get; set; }

		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			ReportStatus($"Installing {Title ?? Url.ToString()}...");

			// Logger stays null: under --json, stdout carries the JSONL stream.
			var boots = new Boots.Core.Bootstrapper
			{
				Url = Url.ToString(),
				Logger = TextWriter.Null
			};

			try
			{
				// Boots elevates with its own `sudo installer`, which cannot prompt without a terminal.
				if (Util.UseMacOsAdministratorPrompt)
					await InstallPkgWithAdministratorPromptAsync(boots, cancellationToken);
				else
					await boots.Install(cancellationToken);

				ReportStatus($"Installed {Title ?? Url.ToString()}.");
			}
			catch (Exception ex)
			{
				Util.Exception(ex);
				ReportStatus($":warning: Installation failed for {Title ?? Url.ToString()}.");
				throw;
			}
		}

		async Task InstallPkgWithAdministratorPromptAsync(Boots.Core.Bootstrapper boots, CancellationToken cancellationToken)
		{
			using var downloader = new Boots.Core.Downloader(boots, ".pkg");

			await Policy
				.Handle<HttpRequestException>()
				.Or<IOException>()
				.RetryAsync(DownloadRetries)
				.ExecuteAsync(downloader.Download, cancellationToken);

			await InstallPkgAsync(
				downloader.TempFile,
				Title ?? Url.ToString(),
				(cmd, args, token) => Util.WrapShellCommandWithSudo(cmd, null, false, token, args),
				cancellationToken);
		}

		/// <summary>
		/// Installs a downloaded macOS package through <paramref name="runElevated"/>, failing
		/// the fix with the command's own output when the install (or its authorization) fails.
		/// </summary>
		internal static async Task InstallPkgAsync(
			string pkgFile,
			string title,
			Func<string, string[], CancellationToken, Task<ShellProcessRunner.ShellProcessResult>> runElevated,
			CancellationToken cancellationToken)
		{
			var result = await runElevated(
				MacInstallerPath,
				new[] { "-verbose", "-dumplog", "-pkg", pkgFile, "-target", "/" },
				cancellationToken);

			Util.ThrowIfFailed(result, $"Installing {title}");
		}
	}
}
