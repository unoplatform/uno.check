using DotNetCheck;
using DotNetCheck.Manifest;

namespace UnoCheck.Tests;

public class ToolInfoValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-version")]
    [InlineData("9999.0.0")]
    public void Validate_WhenToolVersionGateWarnsInNonStrictMode_StillFailsOnIncompatibleSdk(string? toolVersion)
    {
        var manifest = new Manifest
        {
            Check = new Check
            {
                ToolVersion = toolVersion,
                DotNet = new DotNet
                {
                    Sdks =
                    [
                        new DotNetSdk
                        {
                            Version = DotNetSdk.Version6Preview6.ToNormalizedString()
                        }
                    ]
                }
            }
        };

        var result = ToolInfo.Validate(manifest, strictManifest: false);

        Assert.False(result);
    }

    [Fact]
    public void Validate_WhenManifestRequiredVersionIsHigherThanCurrent_AllowsNonStrictMode()
    {
        var manifest = new Manifest
        {
            Check = new Check
            {
                ToolVersion = "9999.0.0"
            }
        };

        var result = ToolInfo.Validate(manifest, strictManifest: false);

        Assert.True(result);
    }

    [Fact]
    public void Validate_WhenManifestRequiredVersionIsHigherThanCurrent_FailsStrictMode()
    {
        var manifest = new Manifest
        {
            Check = new Check
            {
                ToolVersion = "9999.0.0"
            }
        };

        var result = ToolInfo.Validate(manifest, strictManifest: true);

        Assert.False(result);
    }

    [Fact]
    public void Validate_WhenToolVersionMatches_PassesStrictMode()
    {
        var manifest = new Manifest
        {
            Check = new Check
            {
                ToolVersion = ToolInfo.CurrentVersion.ToString()
            }
        };

        var result = ToolInfo.Validate(manifest, strictManifest: true);

        Assert.True(result);
    }

    [Fact]
    public void Validate_WhenToolVersionFormatIsInvalid_AllowsNonStrictMode()
    {
        var manifest = new Manifest
        {
            Check = new Check
            {
                ToolVersion = "not-a-version"
            }
        };

        var result = ToolInfo.Validate(manifest, strictManifest: false);

        Assert.True(result);
    }

    [Fact]
    public void Validate_WhenToolVersionFormatIsInvalid_FailsStrictMode()
    {
        var manifest = new Manifest
        {
            Check = new Check
            {
                ToolVersion = "not-a-version"
            }
        };

        var result = ToolInfo.Validate(manifest, strictManifest: true);

        Assert.False(result);
    }
}
