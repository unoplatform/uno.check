using DotNetCheck;
using DotNetCheck.Checkups;

namespace UnoCheck.Tests;

/// <summary>
/// A failed <c>dotnet dev-certs https --trust</c> has to fail the fix; discarding its exit
/// code reported the certificate as trusted while the recheck still failed.
/// </summary>
public class HttpsDevCertCheckupTests
{
    [Fact]
    public async Task Failed_Trust_Fails_The_Fix_With_Its_Output()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => HttpsDevCertCheckup.TrustDevCertAsync(
            _ => Task.FromResult(new ShellProcessRunner.ShellProcessResult([], ["There was an error trusting the HTTPS developer certificate."], 4)), CancellationToken.None));

        Assert.Contains("There was an error trusting the HTTPS developer certificate.", ex.Message);
        Assert.Contains("4", ex.Message);
    }

    [Fact]
    public async Task Successful_Trust_Completes_The_Fix()
    {
        await HttpsDevCertCheckup.TrustDevCertAsync(
            _ => Task.FromResult(new ShellProcessRunner.ShellProcessResult([], [], 0)), CancellationToken.None);
    }

    [Fact]
    public async Task Trust_Forwards_Cancellation_To_The_Command()
    {
        using var cancellation = new CancellationTokenSource();
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(() => HttpsDevCertCheckup.TrustDevCertAsync(
            token =>
            {
                Assert.Equal(cancellation.Token, token);
                cancellation.Cancel();
                return Task.FromResult(new ShellProcessRunner.ShellProcessResult([], [], -1));
            }, cancellation.Token));

        Assert.Equal(cancellation.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task Canceled_Trust_Does_Not_Start_The_Command()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var called = false;

        await Assert.ThrowsAsync<OperationCanceledException>(() => HttpsDevCertCheckup.TrustDevCertAsync(
            _ =>
            {
                called = true;
                return Task.FromResult(new ShellProcessRunner.ShellProcessResult([], [], 0));
            }, cancellation.Token));

        Assert.False(called);
    }
}
