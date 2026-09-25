using DotNetCheck;
using DotNetCheck.Solutions;
using DotNetCheck.Models;
using System.Net;

namespace UnoCheck.Tests;

/// <summary>
/// Boots runs <c>sudo installer</c> itself, which cannot prompt without a terminal. When the
/// administrator dialog is active, the package install has to go through the elevation seam
/// instead.
/// </summary>
public class BootsSolutionTests
{
    const string PkgFile = "/var/folders/My Temp/it's.pkg";
    const string Hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public async Task Pkg_Install_Runs_The_Installer_Through_The_Elevation_Seam()
    {
        string? command = null;
        string[] arguments = [];

        await BootsSolution.InstallPkgAsync(
            PkgFile,
            Hash,
            "Microsoft OpenJDK 17",
            (cmd, args, _) =>
            {
                command = cmd;
                arguments = args;
                return Task.FromResult(new ShellProcessRunner.ShellProcessResult([], [], 0));
            },
            CancellationToken.None);

        Assert.Equal("/bin/sh", command);
        Assert.Equal(["-c", BootsSolution.InstallScript, "uno-check", PkgFile, Hash], arguments);
        var shell = MacOsAdministratorCommandRunner.BuildCommandLine(command!, arguments);
        Assert.Contains("'/var/folders/My Temp/it'\"'\"'s.pkg'", shell);
        Assert.DoesNotContain("-dumplog", shell);
        Assert.Contains("/private/tmp/uno-check.XXXXXXXX", arguments[1]);
        Assert.True(arguments[1].IndexOf("/bin/cp", StringComparison.Ordinal) < arguments[1].IndexOf("shasum", StringComparison.Ordinal));
        Assert.True(arguments[1].IndexOf("Package integrity check failed", StringComparison.Ordinal) < arguments[1].IndexOf("/usr/sbin/installer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Declined_Authorization_Fails_The_Fix_With_Its_Reason()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => BootsSolution.InstallPkgAsync(
            PkgFile,
            Hash,
            "Download and Install Microsoft OpenJDK 17",
            (_, _, _) => Task.FromResult(new ShellProcessRunner.ShellProcessResult([], ["Administrator approval was declined."], 1)),
            CancellationToken.None));

        Assert.Contains("Microsoft OpenJDK 17", ex.Message);
        Assert.Contains("Administrator approval was declined.", ex.Message);
        Assert.StartsWith("Download and Install Microsoft OpenJDK 17", ex.Message);
    }

    [Fact]
    public async Task Cancellation_During_Install_Waits_For_Completion_And_Cleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var completed = new TaskCompletionSource<ShellProcessRunner.ShellProcessResult>();
        var pending = BootsSolution.InstallPkgAsync(PkgFile, Hash, "JDK", (_, _, token) =>
        {
            Assert.False(token.CanBeCanceled);
            cancellation.Cancel();
            return completed.Task;
        }, cancellation.Token);

        Assert.False(pending.IsCompleted);
        completed.SetResult(new([], [], 0));
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Canceled_Install_Does_Not_Launch_Elevation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var called = false;
        await Assert.ThrowsAsync<OperationCanceledException>(() => BootsSolution.InstallPkgAsync(PkgFile, Hash, "JDK", (_, _, _) =>
        {
            called = true;
            return Task.FromResult(new ShellProcessRunner.ShellProcessResult([], [], 0));
        }, cancellation.Token));
        Assert.False(called);
    }

    [Fact]
    public async Task Implement_Downloads_Reports_Progress_And_Keeps_Package_Until_Installer_Exits()
    {
        using var client = Client((_, _) => Task.FromResult(Response("abc")));
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<ShellProcessRunner.ShellProcessResult>();
        string? package = null;
        var statuses = new List<string>();
        var solution = new BootsSolution(new Uri("https://example.test/sdk.pkg"), "JDK", client, true, (cmd, args, token) =>
        {
            Assert.Equal("/bin/sh", cmd);
            Assert.Equal(Hash, args[4]);
            Assert.False(token.CanBeCanceled);
            package = args[3];
            Assert.Equal("abc", File.ReadAllText(package));
            started.SetResult(true);
            return finished.Task;
        });
        solution.OnStatusUpdated += (_, e) => statuses.Add(e.Message);

        var pending = solution.Implement(new SharedState(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        Assert.False(pending.IsCompleted);
        Assert.True(File.Exists(package));
        finished.SetResult(new([], [], 0));
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);

        Assert.False(File.Exists(package));
        Assert.Contains("Downloading... 100%", statuses);
        Assert.Contains(statuses, text => text.Contains("cancellation waits", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Download_Retries_Transient_Status_With_Backoff(int status)
    {
        var attempts = 0;
        using var client = Client((_, _) => Task.FromResult(++attempts < 3
            ? new HttpResponseMessage((HttpStatusCode)status) : Response("abc")));
        var delays = new List<TimeSpan>();
        var hash = await Download(client, delay: (time, _) => { delays.Add(time); return Task.CompletedTask; });

        Assert.Equal(Hash, hash);
        Assert.Equal(3, attempts);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    public async Task Download_Does_Not_Retry_Permanent_Http_Failures(int status)
    {
        var attempts = 0;
        using var client = Client((_, _) => { attempts++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)); });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Download(client));
        Assert.Contains(status.ToString(), ex.Message);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Download_Retries_Http_Timeout_And_Reports_Timeout_Not_User_Cancellation()
    {
        var attempts = 0;
        using var client = Client((_, _) => { attempts++; throw new TaskCanceledException("HTTP timeout"); });
        await Assert.ThrowsAsync<TimeoutException>(() => Download(client));
        Assert.Equal(4, attempts);
    }

    [Fact]
    public async Task Download_User_Cancellation_Is_Not_Retried()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        using var client = Client((_, token) =>
        {
            attempts++;
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Response("abc"));
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download(client, cancellation.Token));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Download_Bounds_A_Stalled_Response_Body()
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StalledStream())
        }));
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => Download(client, timeout: TimeSpan.FromMilliseconds(50), retries: 0)
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("Package download timed out.", ex.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task Download_Restarts_Partial_Transfers_And_Hashes_Only_The_Final_Bytes()
    {
        var attempts = 0;
        using var client = Client((_, _) => Task.FromResult(++attempts == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream()) }
            : Response("abc")));
        var path = Path.GetTempFileName();
        try
        {
            var hash = await BootsSolution.DownloadPkgAsync(client, new Uri("https://example.test/sdk.pkg"), path,
                3, TimeSpan.FromMinutes(5), _ => { }, (_, _) => Task.CompletedTask, CancellationToken.None);
            Assert.Equal(2, attempts);
            Assert.Equal(Hash, hash);
            Assert.Equal("abc", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Download_Reports_Bytes_When_Content_Length_Is_Unknown()
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent()
        }));
        var statuses = new List<string>();
        var path = Path.GetTempFileName();
        try
        {
            var hash = await BootsSolution.DownloadPkgAsync(client, new Uri("https://example.test/sdk.pkg"), path,
                0, TimeSpan.FromMinutes(5), statuses.Add, (_, _) => Task.CompletedTask, CancellationToken.None);
            Assert.Equal(Hash, hash);
            Assert.Contains("Downloading... 3 bytes", statuses);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Installer_Failure_Is_Preserved_Even_If_Cancellation_Was_Requested()
    {
        using var cancellation = new CancellationTokenSource();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => BootsSolution.InstallPkgAsync(PkgFile, Hash, "JDK", (_, _, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new ShellProcessRunner.ShellProcessResult([], ["Package integrity check failed."], 1));
        }, cancellation.Token));
        Assert.Contains("Package integrity check failed.", ex.Message);
    }

    [Fact]
    public async Task Download_Does_Not_Retry_Local_Io_Failures()
    {
        var attempts = 0;
        using var client = Client((_, _) => { attempts++; return Task.FromResult(Response("abc")); });
        var path = Path.GetTempFileName();
        try
        {
            using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await Assert.ThrowsAsync<IOException>(() => BootsSolution.DownloadPkgAsync(client, new Uri("https://example.test/sdk.pkg"),
                path, 3, TimeSpan.FromMinutes(5), _ => { }, (_, _) => Task.CompletedTask, CancellationToken.None));
            Assert.Equal(1, attempts);
        }
        finally { File.Delete(path); }
    }

    static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new Handler(send));

    static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    static async Task<string> Download(HttpClient client, CancellationToken token = default,
        Func<TimeSpan, CancellationToken, Task>? delay = null, TimeSpan? timeout = null, int retries = 3)
    {
        var file = Path.GetTempFileName();
        try
        {
            return await BootsSolution.DownloadPkgAsync(client, new Uri("https://example.test/sdk.pkg"), file,
                retries, timeout ?? TimeSpan.FromMinutes(5), _ => { }, delay ?? ((_, _) => Task.CompletedTask), token);
        }
        finally { File.Delete(file); }
    }

    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    sealed class StalledStream : MemoryStream
    {
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    sealed class InterruptedStream() : MemoryStream("partial package bytes"u8.ToArray())
    {
        bool first = true;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!first)
                throw new IOException("Connection reset");
            first = false;
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }
    }

    sealed class UnknownLengthContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream("abc"u8.ToArray()));
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync("abc"u8.ToArray()).AsTask();
    }
}
