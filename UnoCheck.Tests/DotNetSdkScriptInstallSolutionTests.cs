using System.Net;
using DotNetCheck.Solutions;

namespace UnoCheck.Tests;

public class DotNetSdkScriptInstallSolutionTests
{
    const string Script = "#!/usr/bin/env bash";

    [Fact]
    public void IsDirectoryWritableOrCreatable_UsesNearestExistingParent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "uno-check-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var nestedPath = Path.Combine(tempDir, "not-created", "dotnet");

            Assert.True(DotNetSdkScriptInstallSolution.IsDirectoryWritableOrCreatable(nestedPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsDirectoryWritableOrCreatable_MissingPath_ReturnsFalse()
    {
        Assert.False(DotNetSdkScriptInstallSolution.IsDirectoryWritableOrCreatable(string.Empty));
    }

    // The dotnet-install script comes from dot.net at the start of every SDK fix. A dropped or slow connection
    // used to fail the whole run: CI jobs failed this way on three platforms on 2026-11-12.
    [Fact]
    public async Task DownloadScript_Retries_A_Dropped_Connection()
    {
        var attempts = 0;
        using var client = Client((_, _) => ++attempts < 3
            ? throw new HttpRequestException("The SSL connection could not be established, see inner exception.")
            : Task.FromResult(Ok()));
        var statuses = new List<string>();

        Assert.Equal(Script, await Download(client, statuses: statuses));

        Assert.Equal(3, attempts);
        Assert.Contains(statuses, status => status.Contains($"retrying from {Fallback.Host} (1/3)"));
        Assert.Contains(statuses, status => status.Contains($"retrying from {Primary.Host} (2/3)"));
    }

    [Fact]
    public async Task DownloadScript_Retries_A_Stalled_Connection()
    {
        var attempts = 0;
        using var client = Client(async (_, token) =>
        {
            if (++attempts == 1)
                await Task.Delay(Timeout.Infinite, token);

            return Ok();
        });

        Assert.Equal(Script, await Download(client, attemptTimeout: TimeSpan.FromMilliseconds(50)).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task DownloadScript_A_Stall_Fails_As_A_Timeout_Not_As_A_Cancellation()
    {
        // CheckCommand reports an OperationCanceledException as "Operation canceled by user." and stops the run.
        using var client = Client(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Ok();
        });

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => Download(client, retries: 1, attemptTimeout: TimeSpan.FromMilliseconds(50))
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("took longer than", ex.Message);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task DownloadScript_Retries_Transient_Status_With_Backoff(int status)
    {
        var attempts = 0;
        using var client = Client((_, _) => Task.FromResult(++attempts < 3 ? new HttpResponseMessage((HttpStatusCode)status) : Ok()));
        var delays = new List<TimeSpan>();

        Assert.Equal(Script, await Download(client, delays: delays));
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task DownloadScript_Fails_Once_The_Retries_Are_Used_Up()
    {
        var attempts = 0;
        using var client = Client((_, _) => { attempts++; throw new HttpRequestException("Connection refused"); });

        await Assert.ThrowsAsync<HttpRequestException>(() => Download(client));
        Assert.Equal(4, attempts);
    }

    [Fact]
    public async Task DownloadScript_Uses_The_First_Host_When_It_Answers()
    {
        var hosts = new List<string>();
        using var client = Client((request, _) => { hosts.Add(request.RequestUri!.Host); return Task.FromResult(Ok()); });

        Assert.Equal(Script, await Download(client));
        Assert.Equal([Primary.Host], hosts);
    }

    [Fact]
    public async Task DownloadScript_Tries_The_Hosts_In_Turn()
    {
        // dot.net stalled from GitHub's runners on 2026-11-12, while builds.dotnet.microsoft.com kept working.
        var hosts = new List<string>();
        using var client = Client((request, _) => { hosts.Add(request.RequestUri!.Host); throw new HttpRequestException("Connection refused"); });
        var statuses = new List<string>();

        await Assert.ThrowsAsync<HttpRequestException>(() => Download(client, statuses: statuses));

        Assert.Equal([Primary.Host, Fallback.Host, Primary.Host, Fallback.Host], hosts);
        Assert.Contains(statuses, status => status.Contains($"retrying from {Fallback.Host} (1/3)"));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    public async Task DownloadScript_Gets_A_Script_Missing_From_One_Host_From_The_Other(int status)
    {
        using var client = Client((request, _) => Task.FromResult(request.RequestUri == Primary ? new HttpResponseMessage((HttpStatusCode)status) : Ok()));

        Assert.Equal(Script, await Download(client));
    }

    [Fact]
    public async Task DownloadScript_User_Cancellation_Is_Not_Retried()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        using var client = Client((_, token) =>
        {
            attempts++;
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Ok());
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download(client, token: cancellation.Token));
        Assert.Equal(1, attempts);
    }

    static readonly Uri Primary = new("https://builds.example.test/dotnet/scripts/v1/dotnet-install.sh");
    static readonly Uri Fallback = new("https://dot.example.test/v1/dotnet-install.sh");

    static Task<string> Download(HttpClient client, List<string>? statuses = null, List<TimeSpan>? delays = null,
        int retries = 3, TimeSpan? attemptTimeout = null, CancellationToken token = default)
        => DotNetSdkScriptInstallSolution.DownloadScriptAsync(client, [Primary, Fallback],
            retries, attemptTimeout ?? TimeSpan.FromSeconds(30), status => statuses?.Add(status),
            (time, _) => { delays?.Add(time); return Task.CompletedTask; }, token);

    static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent(Script) };

    static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new Handler(send));

    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
