using DotNetCheck;
using DotNetCheck.Manifest;

namespace UnoCheck.Tests;

public class ToolInfoTests
{
    [Fact]
    public async Task LoadManifest_LoadsDefaultManifest_WhenNoFileOrUrlProvided()
    {
        // Act
        var manifest = await ToolInfo.LoadManifest(null, ManifestChannel.Default);
        
        // Assert
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Check);
        Assert.NotNull(manifest.Check.ToolVersion);
    }

    [Fact]
    public async Task LoadManifest_LoadsPreviewManifest_WhenPreviewChannelRequested()
    {
        // Act
        var manifest = await ToolInfo.LoadManifest(null, ManifestChannel.Preview);
        
        // Assert
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Check);
        Assert.NotNull(manifest.Check.ToolVersion);
    }

    [Fact]
    public async Task LoadManifest_LoadsPreviewMajorManifest_WhenPreviewMajorChannelRequested()
    {
        // Act
        var manifest = await ToolInfo.LoadManifest(null, ManifestChannel.PreviewMajor);
        
        // Assert
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Check);
        Assert.NotNull(manifest.Check.ToolVersion);
    }

    [Fact]
    public async Task LoadManifest_MainChannel_FallsBackToEmbeddedManifest_WhenMainUrlIsEmpty()
    {
        // Act
        var manifest = await ToolInfo.LoadManifest(null, ManifestChannel.Main, string.Empty);
        
        // Assert
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Check);
        Assert.NotNull(manifest.Check.ToolVersion);
    }

    [Fact]
    public async Task LoadManifest_MainChannel_FallsBackToEmbeddedManifest_WhenRemoteLoadFails()
    {
        var manifest = await ToolInfo.LoadManifest(null, ManifestChannel.Main, "not a valid url");

        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Check);
        Assert.NotEmpty(manifest.Check.ToolVersion);
    }

    [Theory]
    [InlineData(ManifestChannel.Default)]
    [InlineData(ManifestChannel.Preview)]
    [InlineData(ManifestChannel.PreviewMajor)]
    public async Task LoadManifest_LoadsValidManifest_ForAllChannels(ManifestChannel channel)
    {
        // Act
        var manifest = await ToolInfo.LoadManifest(null, channel);
        
        // Assert
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Check);
        Assert.NotEmpty(manifest.Check.ToolVersion);
        Assert.NotNull(manifest.Check.Variables);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void IncludePrereleaseUnoPackages_FollowsManifest(bool? manifestIncludePrerelease, bool expected)
    {
        // Arrange
        var manifest = new Manifest
        {
            Check = new Check
            {
                UnoPackages = manifestIncludePrerelease is { } include ? new UnoPackages { IncludePrerelease = include } : null
            }
        };

        // Act
        var actual = ToolInfo.IncludePrereleaseUnoPackages(manifest);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IncludePrereleaseUnoPackages_NoManifest_UsesStableVersions()
    {
        // Act
        var actual = ToolInfo.IncludePrereleaseUnoPackages(null);

        // Assert
        Assert.False(actual);
    }

    [Fact]
    public void Validate_MissingToolVersion_NonStrict_ReturnsTrue()
    {
        var manifest = new Manifest
        {
            Check = new Check
            {
                ToolVersion = null
            }
        };

        var result = ToolInfo.Validate(manifest, strictManifest: false);

        Assert.True(result);
    }

    [Fact]
    public void Validate_MissingToolVersion_Strict_ReturnsFalse()
    {
        var manifest = new Manifest
        {
            Check = new Check
            {
                ToolVersion = null
            }
        };

        var result = ToolInfo.Validate(manifest, strictManifest: true);

        Assert.False(result);
    }
}
