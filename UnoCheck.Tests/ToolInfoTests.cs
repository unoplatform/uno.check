using DotNetCheck;

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
    public async Task LoadManifest_LoadsMainManifest_WhenMainChannelRequested()
    {
        // Act
        var manifest = await ToolInfo.LoadManifest(null, ManifestChannel.Main);
        
        // Assert
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Check);
        Assert.NotNull(manifest.Check.ToolVersion);
    }

    [Theory]
    [InlineData(ManifestChannel.Default)]
    [InlineData(ManifestChannel.Preview)]
    [InlineData(ManifestChannel.PreviewMajor)]
    [InlineData(ManifestChannel.Main)]
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
}
