using DotNetCheck.Checkups;
using DotNetCheck.Models;

namespace UnoCheck.Tests;

/// <summary>The current user's execution policy is read from the registry, without starting Windows PowerShell.</summary>
public class PSExecutionPolicyCheckupTests
{
    [Theory]
    [InlineData("RemoteSigned")]
    [InlineData("remotesigned")]
    [InlineData(" RemoteSigned ")]
    public void PolicyResult_RemoteSigned_IsOk(string policy)
        => Assert.Equal(Status.Ok, new PSExecutionPolicyCheckup().PolicyResult(policy).Status);

    [Theory]
    [InlineData("Restricted")]
    [InlineData("Unrestricted")]
    [InlineData(null)]
    public void PolicyResult_AnythingElse_OffersToSetRemoteSigned(string? policy)
    {
        var result = new PSExecutionPolicyCheckup().PolicyResult(policy);

        Assert.Equal(Status.Error, result.Status);
        Assert.True(result.HasSuggestion);
    }
}
