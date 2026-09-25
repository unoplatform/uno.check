using DotNetCheck.Checkups;

namespace UnoCheck.Tests;

/// <summary>
/// The Android SDK installer writes straight into the SDK. On macOS/Linux a protected SDK
/// has to be installed into a user-writable staging folder and copied in elevated.
/// </summary>
public class AndroidSdkPackagesCheckupTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void ShouldInstallThroughStaging_Only_Off_Windows_For_A_Protected_Sdk(bool isWindows, bool sdkWritable, bool expected)
        => Assert.Equal(expected, AndroidSdkPackagesCheckup.ShouldInstallThroughStaging(isWindows, sdkWritable));
}
