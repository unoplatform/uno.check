using DotNetCheck.AndroidSdk;

namespace UnoCheck.Tests;

/// <summary>
/// An AVD's .ini file sits next to its folder only in the AVD home. An AVD created elsewhere
/// (avdmanager create avd -p) keeps its .ini in the AVD home, away from the folder.
/// </summary>
public sealed class AvdManagerTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "unocheck-avd-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ParseAvdList_ReadsAnAvdStoredOutsideTheAvdHome()
    {
        var avdPath = CreateAvdFolder("elsewhere", "unoqa.avd", avdId: "unoqa-api35");

        var avds = AvdManager.ParseAvdList(ListOutput("unoqa-api35", avdPath));

        var avd = Assert.Single(avds);
        Assert.Equal("unoqa-api35", avd.Name);
        Assert.Equal(avdPath, avd.Path);
        Assert.Equal("pixel", avd.Device);
        Assert.Equal("Google APIs (Google Inc.)", avd.Target);
        Assert.Equal("system-images;android-35;google_apis;x86_64", avd.SdkId);
    }

    [Fact]
    public void ParseAvdList_SkipsAFolderWithoutConfigIni()
    {
        var avdPath = Path.Join(_root, "empty.avd");
        Directory.CreateDirectory(avdPath);

        Assert.Empty(AvdManager.ParseAvdList(ListOutput("empty", avdPath)));
    }

    [Fact]
    public void From_ReadsTheTargetFromTheIniNextToTheFolder()
    {
        var avdPath = CreateAvdFolder("avd", "Pixel_9_API_35.avd", avdId: "Pixel_9_API_35");
        File.WriteAllLines(Path.Join(_root, "avd", "Pixel_9_API_35.ini"), ["path=" + avdPath, "target=android-35"]);

        var avd = AvdManager.Avd.From(avdPath);

        Assert.NotNull(avd);
        Assert.Equal("Pixel_9_API_35", avd.Name);
        Assert.Equal("android-35", avd.Target);
        Assert.Equal("system-images;android-35;google_apis;x86_64", avd.SdkId);
    }

    [Fact]
    public void From_ReadsAnAvdWhoseIniIsNotNextToTheFolder()
    {
        var avdPath = CreateAvdFolder("elsewhere", "unoqa.avd", avdId: "unoqa-api35");

        var avd = AvdManager.Avd.From(avdPath);

        Assert.NotNull(avd);
        Assert.Equal("unoqa-api35", avd.Name);
        Assert.Equal("pixel", avd.Device);
        Assert.Equal("system-images;android-35;google_apis;x86_64", avd.SdkId);
        Assert.Null(avd.Target);
    }

    [Fact]
    public void From_SkipsIniLinesWithoutAValue()
    {
        var avdPath = CreateAvdFolder("avd", "Pixel_9_API_35.avd", avdId: "Pixel_9_API_35");
        File.AppendAllLines(Path.Join(avdPath, "config.ini"), ["", "not a setting"]);

        var avd = AvdManager.Avd.From(avdPath);

        Assert.NotNull(avd);
        Assert.Equal("Pixel_9_API_35", avd.Name);
    }

    [Fact]
    public void ListAvds_ReturnsNothingWhenTheSdkHasNoAvdManager()
    {
        var sdkPath = Directory.CreateDirectory(Path.Join(_root, "sdk")).FullName;

        Assert.Empty(new AvdManager(Path.Join(_root, "java"), sdkPath).ListAvds());
    }

    private string CreateAvdFolder(string parent, string folder, string avdId)
    {
        var avdPath = Path.Join(_root, parent, folder);
        Directory.CreateDirectory(avdPath);
        File.WriteAllLines(Path.Join(avdPath, "config.ini"),
        [
            "AvdId=" + avdId,
            "hw.device.name=pixel",
            @"image.sysdir.1=system-images\android-35\google_apis\x86_64\",
        ]);

        return avdPath;
    }

    // The output of `avdmanager list avd` (cmdline-tools 19.0), joined with \n as AvdManager.ListAvds does.
    private static string ListOutput(string name, string avdPath)
        => string.Join("\n",
            "Available Android Virtual Devices:",
            "    Name: " + name,
            "  Device: pixel (Google)",
            "    Path: " + avdPath,
            "  Target: Google APIs (Google Inc.)",
            "          Based on: Android 15.0 (\"VanillaIceCream\") Tag/ABI: google_apis/x86_64",
            "  Sdcard: 512 MB");
}
