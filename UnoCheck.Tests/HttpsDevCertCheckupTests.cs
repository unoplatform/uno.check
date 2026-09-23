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
            () => Task.FromResult(new ShellProcessRunner.ShellProcessResult([], ["There was an error trusting the HTTPS developer certificate."], 4))));

        Assert.Contains("There was an error trusting the HTTPS developer certificate.", ex.Message);
        Assert.Contains("4", ex.Message);
    }

    [Fact]
    public async Task Successful_Trust_Completes_The_Fix()
    {
        await HttpsDevCertCheckup.TrustDevCertAsync(
            () => Task.FromResult(new ShellProcessRunner.ShellProcessResult([], [], 0)));
    }
}
