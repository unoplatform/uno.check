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
	public void Write_ThenFlushAndCloseInput_DeliversBytesAndEofToChild()
	{
		// Repro for the sudo -S flow: child must receive the piped bytes AND see EOF
		// on stdin so it can terminate. Using `cat` / `findstr` as a stand-in for sudo:
		// they read stdin until EOF and echo to stdout.
		var (executable, args) = GetStdinEchoCommand();

		var sut = new ShellProcessRunner(new ShellProcessRunnerOptions(executable, args)
		{
			UseSystemShell = false,
			RedirectInput = true,
			RedirectOutput = true,
		});

		sut.Write("hello-from-stdin\n");
		sut.FlushAndCloseInput();

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		while (!sut.HasExited && !cts.IsCancellationRequested)
		{
			Thread.Sleep(50);
		}

		var result = sut.WaitForExit();
		var output = string.Join(Environment.NewLine, result.StandardOutput);

		Assert.False(cts.IsCancellationRequested, "Child process did not terminate after EOF on stdin.");
		Assert.Contains("hello-from-stdin", output);
		Assert.Equal(0, result.ExitCode);
	}

	[Fact]
	public void FlushAndCloseInput_WhenRedirectInputDisabled_IsNoOp()
	{
		// Caller without RedirectInput must not throw — the helper should detect the
		// configuration and short-circuit before touching StandardInput.
		var (executable, args) = GetShellCommand(
			"echo done",
			"echo done");

		var sut = new ShellProcessRunner(new ShellProcessRunnerOptions(executable, args)
		{
			UseSystemShell = false,
			RedirectInput = false,
			RedirectOutput = true,
		});

		var ex = Record.Exception(() => sut.FlushAndCloseInput());
		var result = sut.WaitForExit();

		Assert.Null(ex);
		Assert.Equal(0, result.ExitCode);
	}

	[Fact]
	public void FlushAndCloseInput_AfterProcessExited_IsNoOp()
	{
		// In the sudo -S flow the child can exit before we write the password
		// (e.g., sudo not installed). FlushAndCloseInput must not throw in that case.
		var (executable, args) = GetShellCommand(
			"echo quick && exit 0",
			"echo quick & exit /b 0");

		var sut = new ShellProcessRunner(new ShellProcessRunnerOptions(executable, args)
		{
			UseSystemShell = false,
			RedirectInput = true,
			RedirectOutput = true,
		});

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		while (!sut.HasExited && !cts.IsCancellationRequested)
		{
			Thread.Sleep(50);
		}

		Assert.True(sut.HasExited, "Test precondition: child should have exited quickly.");

		var ex = Record.Exception(() => sut.FlushAndCloseInput());

		Assert.Null(ex);
	}

	[Fact]
	public void FlushAndCloseInput_CalledTwice_DoesNotThrow()
	{
		// Defensive: a caller that double-invokes the helper (e.g., on cleanup paths)
		// must not see the second call surface an ObjectDisposedException.
		var (executable, args) = GetStdinEchoCommand();

		var sut = new ShellProcessRunner(new ShellProcessRunnerOptions(executable, args)
		{
			UseSystemShell = false,
			RedirectInput = true,
			RedirectOutput = true,
		});

		sut.Write("ping\n");
		sut.FlushAndCloseInput();
		var ex = Record.Exception(() => sut.FlushAndCloseInput());
		sut.WaitForExit();

		Assert.Null(ex);
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

	private static (string executable, string args) GetStdinEchoCommand()
	{
		// Reads stdin until EOF and echoes to stdout — a stand-in for sudo's
		// "consume bytes piped via -S then run the child" behavior.
		if (OperatingSystem.IsWindows())
		{
			return ("findstr.exe", "/R \".*\"");
		}

		return ("/bin/cat", string.Empty);
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