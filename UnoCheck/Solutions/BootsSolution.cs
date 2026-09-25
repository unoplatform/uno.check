using DotNetCheck.Models;
using Polly;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class BootsSolution : Solution
	{
		// Bound each network read, rather than the whole SDK download on slow connections.
		static readonly TimeSpan DownloadStallTimeout = TimeSpan.FromMinutes(5);
		static readonly HttpClient DownloadClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
		readonly HttpClient downloadClient;
		readonly bool isMac;
		readonly Func<string, string[], CancellationToken, Task<ShellProcessRunner.ShellProcessResult>> runPackage;
		// Run in a root-owned directory and verify the bytes AFTER copying, so a file
		// replaced while the authorization dialog is open cannot be installed as root.
		internal const string InstallScript = "set -eu\n"
			+ "umask 077\n"
			+ "stage=$(/usr/bin/mktemp -d /private/tmp/uno-check.XXXXXXXX)\n"
			+ "trap '/bin/rm -rf \"$stage\"' EXIT\n"
			+ "/bin/cp \"$1\" \"$stage/package.pkg\"\n"
			+ "actual=$(/usr/bin/shasum -a 256 \"$stage/package.pkg\")\n"
			+ "[ \"${actual%% *}\" = \"$2\" ] || { echo 'Package integrity check failed.' >&2; exit 1; }\n"
			+ "/usr/sbin/installer -pkg \"$stage/package.pkg\" -target /\n";

		public BootsSolution(Uri url, string title)
			: this(url, title, DownloadClient, Util.IsMac, RunPackageCommandAsync)
		{
		}

		internal BootsSolution(Uri url, string title, HttpClient downloadClient, bool isMac,
			Func<string, string[], CancellationToken, Task<ShellProcessRunner.ShellProcessResult>> runPackage)
		{
			Url = url;
			Title = title;
			this.downloadClient = downloadClient;
			this.isMac = isMac;
			this.runPackage = runPackage;
		}

		public Uri Url { get; set; }
		public string Title { get; set; }
		string DisplayName => Title ?? Url.ToString();

		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			ReportStatus($"Installing {DisplayName}...");

			// Keep Boots' console output out of Uno.Check's output.
			var boots = new Boots.Core.Bootstrapper
			{
				Url = Url.ToString(),
				Logger = TextWriter.Null
			};

			try
			{
				// All macOS packages share the download and elevation path, including terminals.
				if (isMac)
					await InstallMacPackageAsync(boots.NetworkRetries, cancellationToken);
				else
					await boots.Install(cancellationToken);

				ReportStatus($"Installed {DisplayName}.");
			}
			catch (Exception ex)
			{
				Util.Exception(ex);
				ReportStatus($":warning: Installation failed for {DisplayName}.");
				throw;
			}
		}

		async Task InstallMacPackageAsync(int retries, CancellationToken cancellationToken)
		{
			var pkgFile = Path.GetTempFileName();
			try
			{
				var hash = await DownloadPkgAsync(downloadClient, Url, pkgFile, retries, DownloadStallTimeout,
					ReportStatus, Task.Delay, cancellationToken);
				ReportStatus(Util.UseMacOsAdministratorPrompt
					? "Waiting for administrator approval. Once approved, cancellation waits for installation to finish."
					: "Installing package. Cancellation waits for installation to finish.");
				await InstallPkgAsync(pkgFile, hash, DisplayName, runPackage, cancellationToken);
			}
			finally
			{
				File.Delete(pkgFile);
			}
		}

		internal static Task<ShellProcessRunner.ShellProcessResult> RunPackageCommandAsync(
			string cmd, string[] args, CancellationToken cancellationToken)
			=> Util.IsAdmin()
				? new ShellProcessRunner(new ShellProcessRunnerOptions(cmd, string.Empty, cancellationToken)
				{
					ArgumentList = args,
					UseSystemShell = false
				}).WaitForExitAsync()
				: Util.WrapShellCommandWithSudo(cmd, null, false, cancellationToken, args);

		internal static async Task<string> DownloadPkgAsync(HttpClient client, Uri url, string pkgFile,
			int retries, TimeSpan stallTimeout, Action<string> reportStatus,
			Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
		{
			string hash = null;
			await Policy.Handle<HttpRequestException>().Or<TimeoutException>()
				.RetryAsync(retries, async (exception, attempt) =>
				{
					// Short exponential backoff avoids hammering a temporarily unavailable server.
					reportStatus($"Download interrupted; retrying ({attempt}/{retries})...");
					await delay(TimeSpan.FromSeconds(Math.Pow(2, Math.Min(attempt - 1, 4))), cancellationToken);
				})
				.ExecuteAsync(async token =>
				{
					using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
					timeout.CancelAfter(stallTimeout);
					try
					{
						reportStatus("Downloading package...");
						using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
						var status = (int)response.StatusCode;
						if (status == 408 || status == 429 || status >= 500)
							throw new HttpRequestException($"Package download returned HTTP {status}.");
						if (!response.IsSuccessStatusCode)
							throw new InvalidOperationException($"Package download returned HTTP {status} ({response.ReasonPhrase}).");

						using var input = await response.Content.ReadAsStreamAsync();
						using var output = new FileStream(pkgFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
						using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
						var buffer = new byte[81920];
						long received = 0;
						var lastProgress = -1L;
						var length = response.Content.Headers.ContentLength;
						while (true)
						{
							timeout.CancelAfter(stallTimeout);
							int read;
							try
							{
								read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token);
							}
							catch (IOException ex)
							{
								throw new HttpRequestException("Package download stream was interrupted.", ex);
							}
							if (read == 0)
								break;
							await output.WriteAsync(buffer, 0, read, timeout.Token);
							sha.AppendData(buffer, 0, read);
							received += read;
							var progress = length > 0 ? received * 100 / length.Value / 10 : received / (1024 * 1024);
							if (progress != lastProgress)
							{
								lastProgress = progress;
								reportStatus(length > 0 ? $"Downloading... {Math.Min(progress * 10, 100)}%" : $"Downloading... {received} bytes");
							}
						}
						if (length.HasValue && received != length.Value)
							throw new HttpRequestException("Package download was incomplete.");
						hash = BitConverter.ToString(sha.GetHashAndReset()).Replace("-", "").ToLowerInvariant();
					}
					catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
					{
						throw new TimeoutException("Package download timed out.", ex);
					}
				}, cancellationToken);
			return hash;
		}

		/// <summary>
		/// Installs a downloaded macOS package through <paramref name="runElevated"/>, failing
		/// the fix with the command's own output when the install (or its authorization) fails.
		/// </summary>
		internal static async Task InstallPkgAsync(
			string pkgFile,
			string sha256,
			string title,
			Func<string, string[], CancellationToken, Task<ShellProcessRunner.ShellProcessResult>> runElevated,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			// A non-root parent cannot reliably kill the authorized root installer. Wait
			// for it (and its cleanup) before honoring cancellation or deleting the source.
			var result = await runElevated(
				"/bin/sh",
				new[] { "-c", InstallScript, "uno-check", pkgFile, sha256 },
				CancellationToken.None);

			Util.ThrowIfFailed(result, title);
			cancellationToken.ThrowIfCancellationRequested();
		}
	}
}
