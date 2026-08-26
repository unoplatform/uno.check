using DotNetCheck;
using Xunit;

namespace UnoCheck.Tests;

public class InteractiveSelectorTests
{
	[Fact]
	public void ShouldShowInteractivePrompts_ReturnsFalse_WhenCIIsTrue()
	{
		// Arrange
		var settings = new CheckSettings
		{
			CI = true
		};

		// Act
		var result = InteractiveSelector.ShouldShowInteractivePrompts(settings);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void ShouldShowInteractivePrompts_ReturnsFalse_WhenNonInteractiveIsTrue()
	{
		// Arrange
		var settings = new CheckSettings
		{
			NonInteractive = true
		};

		// Act
		var result = InteractiveSelector.ShouldShowInteractivePrompts(settings);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void ShouldShowInteractivePrompts_ReturnsFalse_WhenIdeIsSpecified()
	{
		// Arrange
		var settings = new CheckSettings
		{
			Ide = "vs"
		};

		// Act
		var result = InteractiveSelector.ShouldShowInteractivePrompts(settings);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void ShouldShowInteractivePrompts_ReturnsFalse_WhenTargetPlatformsAreSpecified()
	{
		// Arrange
		var settings = new CheckSettings
		{
			TargetPlatforms = new[] { "windows", "android" }
		};

		// Act
		var result = InteractiveSelector.ShouldShowInteractivePrompts(settings);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void ShouldShowInteractivePrompts_ReturnsFalse_WhenFrameworksAreSpecified()
	{
		// Arrange
		var settings = new CheckSettings
		{
			Frameworks = new[] { "net8.0-android" }
		};

		// Act
		var result = InteractiveSelector.ShouldShowInteractivePrompts(settings);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void ShouldShowInteractivePrompts_ReturnsFalse_WhenSkipIsSpecified()
	{
		// Arrange
		var settings = new CheckSettings
		{
			Skip = new[] { "vswin" }
		};

		// Act
		var result = InteractiveSelector.ShouldShowInteractivePrompts(settings);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void ShouldShowInteractivePrompts_ReturnsTrue_WhenNoOptionsAreSpecified()
	{
		// Arrange
		var settings = new CheckSettings();

		// Act
		var result = InteractiveSelector.ShouldShowInteractivePrompts(settings);

		// Assert
		Assert.True(result);
	}

	[Fact]
	public void ShouldShowInteractivePrompts_ReturnsFalse_WhenMultipleOptionsAreSpecified()
	{
		// Arrange
		var settings = new CheckSettings
		{
			Ide = "vscode",
			TargetPlatforms = new[] { "webassembly" }
		};

		// Act
		var result = InteractiveSelector.ShouldShowInteractivePrompts(settings);

		// Assert
		Assert.False(result);
	}
}
