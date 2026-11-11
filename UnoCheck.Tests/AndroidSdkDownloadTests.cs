using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DotNetCheck.Checkups;

namespace UnoCheck.Tests;

/// <summary>
/// Android SDK archives are downloaded from dl.google.com, where a system image is ~1.5 GB. A dropped
/// connection has to be retried and resumed from the bytes already written, not fail the whole fix.
/// </summary>
public class AndroidSdkDownloadTests
{
    const string Body = "0123456789";
    static readonly EntityTagHeaderValue ETag = new("\"v1\"");

    [Fact]
    public async Task Download_Resumes_A_Dropped_Transfer_From_The_Bytes_Already_Written()
    {
        var ranges = new List<(long? From, EntityTagHeaderValue? IfRange)>();
        using var client = Client((request, _) =>
        {
            var from = request.Headers.Range?.Ranges.Single().From;
            ranges.Add((from, request.Headers.IfRange?.EntityTag));
            return Task.FromResult(from is long start ? Partial(start) : Full(cutAfter: 4));
        });
        var statuses = new List<string>();

        Assert.Equal(Body, await Download(client, statuses: statuses));

        Assert.Equal([(null, null), (4, ETag)], ranges);
        Assert.Contains(statuses, status => status.Contains("retrying (1/3)"));
    }

    [Fact]
    public async Task Download_Starts_Over_When_The_Server_Ignores_The_Range()
    {
        var attempts = 0;
        using var client = Client((_, _) => Task.FromResult(++attempts == 1 ? Full(cutAfter: 4) : Full()));

        Assert.Equal(Body, await Download(client));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Download_Resumes_A_Body_Shorter_Than_The_Expected_Size()
    {
        // Without a Content-Length, a cut connection can look like the end of the body.
        using var client = Client((request, _) => Task.FromResult(request.Headers.Range is { } range
            ? Partial(range.Ranges.Single().From!.Value)
            : Full(body: Body[..4], knownLength: false)));

        Assert.Equal(Body, await Download(client, expectedSize: Body.Length));
    }

    [Fact]
    public async Task Download_Fails_Once_The_Retries_Are_Used_Up()
    {
        var attempts = 0;
        using var client = Client((_, _) => { attempts++; return Task.FromResult(Full(cutAfter: 4)); });

        await Assert.ThrowsAsync<HttpRequestException>(() => Download(client));
        Assert.Equal(4, attempts);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Download_Retries_Transient_Status_With_Backoff(int status)
    {
        var attempts = 0;
        using var client = Client((_, _) => Task.FromResult(++attempts < 3 ? new HttpResponseMessage((HttpStatusCode)status) : Full()));
        var delays = new List<TimeSpan>();

        Assert.Equal(Body, await Download(client, delays: delays));
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
    public async Task Download_User_Cancellation_Is_Not_Retried()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        using var client = Client((_, token) =>
        {
            attempts++;
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Full());
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download(client, token: cancellation.Token));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Download_Bounds_A_Stalled_Response_Body()
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StalledStream())
        }));

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => Download(client, retries: 0, stallTimeout: TimeSpan.FromMilliseconds(50))
            .WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("stalled", ex.Message);
    }

    static async Task<string> Download(HttpClient client, long expectedSize = 0, List<string>? statuses = null,
        List<TimeSpan>? delays = null, int retries = 3, TimeSpan? stallTimeout = null, CancellationToken token = default)
    {
        var path = Path.GetTempFileName();
        try
        {
            await AndroidSdkPackagesCheckup.DownloadArchiveAsync(client, new Uri("https://dl.example.test/repository/x86_64-35_r09.zip"),
                path, expectedSize, retries, stallTimeout ?? TimeSpan.FromMinutes(5), status => statuses?.Add(status),
                (time, _) => { delays?.Add(time); return Task.CompletedTask; }, token);

            return await File.ReadAllTextAsync(path);
        }
        finally { File.Delete(path); }
    }

    static HttpResponseMessage Full(string body = Body, int? cutAfter = null, bool knownLength = true)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Content(body, cutAfter, knownLength) };
        response.Headers.ETag = ETag;
        return response;
    }

    static HttpResponseMessage Partial(long from)
    {
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = Content(Body[(int)from..], null, true) };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, Body.Length - 1, Body.Length);
        response.Headers.ETag = ETag;
        return response;
    }

    static StreamContent Content(string body, int? cutAfter, bool knownLength)
    {
        var content = new StreamContent(new CutStream(Encoding.ASCII.GetBytes(body), cutAfter));
        if (knownLength)
            content.Headers.ContentLength = body.Length;
        return content;
    }

    static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new Handler(send));

    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    /// <summary>
    /// A response body that drops the connection after <c>cutAfter</c> bytes, as HttpClient reports
    /// a body shorter than its Content-Length. Not seekable, so StreamContent can't compute a length.
    /// </summary>
    sealed class CutStream(byte[] bytes, int? cutAfter) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (cutAfter is int cut)
            {
                if (Position >= cut)
                    throw new IOException("The response ended prematurely.");
                count = (int)Math.Min(count, cut - Position);
            }

            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            var bytes = new byte[buffer.Length];
            var read = Read(bytes, 0, bytes.Length);
            bytes.AsSpan(0, read).CopyTo(buffer);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => new(Read(buffer.Span));
    }

    sealed class StalledStream : MemoryStream
    {
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
