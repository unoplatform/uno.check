using System.Diagnostics;
using DotNetCheck;

namespace UnoCheck.Tests;

public class ShellProcessRunnerTests
{
	[Fact]
	public void WaitForExit_CapturesStdOutAndStdErr_AndReturnsExitCode()
	{
		var (executable, args) = GetShellCommand(
			"echo out-line && echo err-line 1>&2 && exit 7",
			"echo out-line & echo err-line 1>&2 & exit /b 7");

		var sut = new ShellProcessRunner(new ShellProcessRunnerOptions(executable, args)
		{
			UseSystemShell = false,
			RedirectOutput = true,
		});

		var result = sut.WaitForExit();

		Assert.Contains("out-line", string.Join(Environment.NewLine, result.StandardOutput));
		Assert.Contains("err-line", string.Join(Environment.NewLine, result.StandardError));
		Assert.Equal(7, result.ExitCode);
		Assert.False(result.Success);
	}

	[Fact]
	public void WaitForExit_UsesTemporaryWorkingDirectory_AndEnvironmentVariables()
	{
		using var tempDir = new TemporaryDirectory();
		var expectedToken = Guid.NewGuid().ToString("N");

		var (executable, args) = GetShellCommand(
			"pwd && echo $UNOCHECK_TEST_TOKEN",
			"cd & echo %UNOCHECK_TEST_TOKEN%");

		var sut = new ShellProcessRunner(new ShellProcessRunnerOptions(executable, args)
		{
			UseSystemShell = false,
			RedirectOutput = true,
			WorkingDirectory = tempDir.Path,
			EnvironmentVariables = new Dictionary<string, string>
			{
				["UNOCHECK_TEST_TOKEN"] = expectedToken,
			},
		});

		var result = sut.WaitForExit();
		var output = string.Join(Environment.NewLine, result.StandardOutput);

		Assert.Equal(0, result.ExitCode);
		Assert.Contains(expectedToken, output);

		var expectedDirectory = Path.GetFullPath(tempDir.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var normalizedOutput = output
			.Replace("\\", "/", StringComparison.Ordinal)
			.Replace("\r", string.Empty, StringComparison.Ordinal)
			.ToLowerInvariant();
		var normalizedExpectedDirectory = expectedDirectory
			.Replace("\\", "/", StringComparison.Ordinal)
			.ToLowerInvariant();

		Assert.Contains(normalizedExpectedDirectory, normalizedOutput);
	}

	[Fact]
	public void WaitForExit_CancellationToken_KillsLongRunningCommand()
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
		var sw = Stopwatch.StartNew();

		var (executable, args) = GetShellCommand(
			"echo started && sleep 30 && echo completed",
			"echo started & ping 127.0.0.1 -n 30 > nul & echo completed");

		var sut = new ShellProcessRunner(new ShellProcessRunnerOptions(executable, args, cts.Token)
		{
			UseSystemShell = false,
			RedirectOutput = true,
		});

		var result = sut.WaitForExit();
		sw.Stop();

		var output = string.Join(Environment.NewLine, result.StandardOutput);

		Assert.Contains("started", output);
		Assert.DoesNotContain("completed", output);
		Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Expected cancellation before 10s but took {sw.Elapsed}.");
		Assert.NotEqual(0, result.ExitCode);
	}

	[Fact]
	public void WaitForExit_CapturesIntermittentOutputWithoutHanging()
	{
		var (executable, args) = GetShellCommand(
			"echo retry-1 && sleep 1 && echo retry-2 && sleep 1 && echo completed",
			"echo retry-1 & ping 127.0.0.1 -n 2 > nul & echo retry-2 & ping 127.0.0.1 -n 2 > nul & echo completed");

		var sut = new ShellProcessRunner(new ShellProcessRunnerOptions(executable, args)
		{
			UseSystemShell = false,
			RedirectOutput = true,
		});

		var result = sut.WaitForExit();
		var output = string.Join(Environment.NewLine, result.StandardOutput);

		Assert.True(result.Success);
		Assert.Contains("retry-1", output);
		Assert.Contains("retry-2", output);
		Assert.Contains("completed", output);
	}

	[Fact]
	public void WaitForExit_CancellationToken_KillsNeverEndingCommand()
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
		var sw = Stopwatch.StartNew();

		var (executable, args) = GetNeverEndingCommand();

		var sut = new ShellProcessRunner(new ShellProcessRunnerOptions(executable, args, cts.Token)
		{
			UseSystemShell = false,
			RedirectOutput = true,
		});

		var result = sut.WaitForExit();
		sw.Stop();

		var output = string.Join(Environment.NewLine, result.StandardOutput);

		Assert.Contains("started", output);
		Assert.True(sw.Elapsed < TimeSpan.FromSeconds(12), $"Expected cancellation before 12s but took {sw.Elapsed}.");
		Assert.NotEqual(0, result.ExitCode);
	}

	[Fact]
	public void Run_ReturnsSuccess_ForSimpleCommand()
	{
		var (executable, args) = GetShellCommand(
			"echo hello",
			"echo hello");

		var result = ShellProcessRunner.Run(executable, args);

		Assert.True(result.Success);
		Assert.Contains("hello", string.Join(Environment.NewLine, result.StandardOutput));
	}

	private static (string executable, string args) GetShellCommand(string unixCommand, string windowsCommand)
	{
		if (OperatingSystem.IsWindows())
		{
			return ("cmd.exe", $"/d /c \"{windowsCommand}\"");
		}

		return ("/bin/sh", $"-c \"{unixCommand}\"");
	}

	private static (string executable, string args) GetNeverEndingCommand()
	{
		if (OperatingSystem.IsWindows())
		{
			return ("powershell.exe", "-NoProfile -Command \"Write-Output 'started'; while ($true) { Start-Sleep -Milliseconds 200 }\"");
		}

		return ("/bin/sh", "-c \"echo started; tail -f /dev/null\"");
	}

	private sealed class TemporaryDirectory : IDisposable
	{
		public TemporaryDirectory()
		{
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uno-check-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}

		public string Path { get; }

		public void Dispose()
		{
			try
			{
				if (Directory.Exists(Path))
				{
					Directory.Delete(Path, recursive: true);
				}
			}
			catch
			{
			}
		}
	}
}