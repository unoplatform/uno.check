using DotNetCheck.Manifest;

namespace UnoCheck.Tests;

public class ManifestTests
{
    [Fact]
    public async Task FromEmbeddedResource_LoadsDefaultManifest_Successfully()
    {
        // Act
        var manifest = await Manifest.FromEmbeddedResource(Manifest.DefaultManifestResourceName);
        
        // Assert
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Check);
        Assert.NotNull(manifest.Check.ToolVersion);
    }

    [Fact]
    public async Task FromEmbeddedResource_LoadsPreviewManifest_Successfully()
    {
        // Act
        var manifest = await Manifest.FromEmbeddedResource(Manifest.PreviewManifestResourceName);
        
        // Assert
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Check);
        Assert.NotNull(manifest.Check.ToolVersion);
    }

    [Fact]
    public async Task FromEmbeddedResource_LoadsPreviewMajorManifest_Successfully()
    {
        // Act
        var manifest = await Manifest.FromEmbeddedResource(Manifest.PreviewMajorManifestResourceName);
        
        // Assert
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Check);
        Assert.NotNull(manifest.Check.ToolVersion);
    }

    [Fact]
    public async Task FromEmbeddedResource_ThrowsException_WhenResourceNotFound()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Manifest.FromEmbeddedResource("non.existent.manifest.json"));
    }

    [Theory]
    [InlineData("uno.ui.manifest.json")]
    [InlineData("uno.ui-preview.manifest.json")]
    [InlineData("uno.ui-preview-major.manifest.json")]
    public async Task FromEmbeddedResource_ManifestHasValidStructure(string resourceName)
    {
        // Act
        var manifest = await Manifest.FromEmbeddedResource(resourceName);
        
        // Assert
        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Check);
        Assert.NotEmpty(manifest.Check.ToolVersion);
        Assert.NotNull(manifest.Check.Variables);
    }
}
