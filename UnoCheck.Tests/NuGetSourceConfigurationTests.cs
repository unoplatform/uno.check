using DotNetCheck;
using DotNetCheck.DotNet;
using System.Xml.Linq;
using NuGet.Configuration;
using NuGet.Versioning;

namespace UnoCheck.Tests;

/// <summary>
/// uno-check resolves package sources from the NuGet configuration instead of a fixed public feed, so it works for
/// repositories that use another feed, or clear the default sources.
/// </summary>
public sealed class NuGetSourceConfigurationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("uno-check-nuget-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private NuGetConfiguration LoadConfiguration(string nugetConfigContents)
    {
        File.WriteAllText(Path.Combine(_directory, "nuget.config"), nugetConfigContents);

        // Only this file, so the machine's own configuration cannot influence the result.
        var settings = Settings.LoadSpecificSettings(_directory, ScratchNuGetConfig.FileName);
        return NuGetSourceResolver.Load(settings);
    }

    [Fact]
    public void ClearedConfigurationOnlyYieldsItsOwnSources()
    {
        var configuration = LoadConfiguration(
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="internal" value="https://feed.example/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var source = Assert.Single(configuration.Sources);
        Assert.Equal("internal", source.Name);
        Assert.Equal("https://feed.example/v3/index.json", source.Source);
    }

    [Fact]
    public void DisabledSourcesAreIgnored()
    {
        var configuration = LoadConfiguration(
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="internal" value="https://feed.example/v3/index.json" />
                <add key="public" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <disabledPackageSources>
                <add key="public" value="true" />
              </disabledPackageSources>
            </configuration>
            """);

        Assert.Equal(["internal"], configuration.Sources.Select(s => s.Name));
    }

    [Fact]
    public void ConfigurationWithoutEnabledSourcesSkipsLookups()
    {
        var configuration = LoadConfiguration("<configuration><packageSources><clear /></packageSources></configuration>");

        Assert.Empty(NuGetSourceResolver.GetLookupSources(configuration));
    }

    [Fact]
    public void UnreadableConfigurationFallsBackToTheDefaultSource()
    {
        var source = Assert.Single(NuGetSourceResolver.GetLookupSources(null));

        Assert.Equal("https://api.nuget.org/v3/index.json", source.Source);
    }

    [Fact]
    public async Task LatestVersionIsTheHighestAcrossSourcesThatAnswer()
    {
        PackageSource[] sources = [new("https://a.example/index.json", "a"), new("https://b.example/index.json", "b")];

        var latest = await NuGetHelper.GetLatestPackageVersionAsync(
            sources,
            (source, _) => Task.FromResult<IEnumerable<NuGetVersion>>(source.Name == "a" ? [NuGetVersion.Parse("1.0.0")] : [NuGetVersion.Parse("2.0.0-pre"), NuGetVersion.Parse("1.5.0")]),
            TimeSpan.FromSeconds(5));

        Assert.Equal(NuGetVersion.Parse("2.0.0-pre"), latest);
    }

    [Fact]
    public async Task UnreachableSourceDoesNotFailTheLookup()
    {
        PackageSource[] sources = [new("https://a.example/index.json", "a"), new("https://b.example/index.json", "b")];

        var latest = await NuGetHelper.GetLatestPackageVersionAsync(
            sources,
            (source, _) => source.Name == "a"
                ? throw new HttpRequestException("unreachable")
                : Task.FromResult<IEnumerable<NuGetVersion>>([NuGetVersion.Parse("3.1.0")]),
            TimeSpan.FromSeconds(5));

        Assert.Equal(NuGetVersion.Parse("3.1.0"), latest);
    }

    [Fact]
    public async Task LookupYieldsNothingWhenNoSourceCanAnswer()
    {
        PackageSource[] sources = [new("https://a.example/index.json", "a")];

        Assert.Null(await NuGetHelper.GetLatestPackageVersionAsync(
            sources, (_, _) => throw new HttpRequestException("unreachable"), TimeSpan.FromSeconds(5)));

        Assert.Null(await NuGetHelper.GetLatestPackageVersionAsync(
            [], (_, _) => throw new InvalidOperationException("no source to query"), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task HungSourceIsAbandonedAfterTheTimeout()
    {
        PackageSource[] sources = [new("https://a.example/index.json", "a")];

        var latest = await NuGetHelper.GetLatestPackageVersionAsync(
            sources,
            async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return [];
            },
            TimeSpan.FromMilliseconds(50));

        Assert.Null(latest);
    }

    [Fact]
    public void ManifestSourcesExtendTheConfiguredOnesWithoutForcingThePublicFeed()
    {
        PackageSource[] configured = [new("https://feed.example/v3/index.json/", "internal")];

        var merged = NuGetSourceResolver.MergeManifestSources(
            configured,
            [
                "https://pkgs.example/dotnet/index.json",
                "https://api.nuget.org/v3/index.json",
                "https://FEED.example/v3/index.json",
            ]);

        Assert.Equal(["https://feed.example/v3/index.json/", "https://pkgs.example/dotnet/index.json"], merged.Select(s => s.Source));
    }

    [Fact]
    public void ManifestSourcesKeepThePublicFeedWhenItIsConfigured()
    {
        PackageSource[] configured = [new("https://api.nuget.org/v3/index.json", "nuget.org")];

        var merged = NuGetSourceResolver.MergeManifestSources(configured, ["https://api.nuget.org/v3/index.json", "https://pkgs.example/dotnet/index.json"]);

        Assert.Equal(["https://api.nuget.org/v3/index.json", "https://pkgs.example/dotnet/index.json"], merged.Select(s => s.Source));
    }

    [Fact]
    public void ScratchConfigurationCarriesTheSourcesButNoCredentials()
    {
        var configuration = LoadConfiguration(
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="internal" value="https://feed.example/v3/index.json" />
              </packageSources>
              <packageSourceCredentials>
                <internal>
                  <add key="Username" value="user" />
                  <add key="ClearTextPassword" value="secret" />
                </internal>
              </packageSourceCredentials>
            </configuration>
            """);
        var scratch = Directory.CreateDirectory(Path.Combine(_directory, "scratch")).FullName;

        var path = ScratchNuGetConfig.Write(scratch, configuration.Sources, configuration.GlobalPackagesFolder);

        var contents = File.ReadAllText(path);
        Assert.DoesNotContain("secret", contents);
        Assert.DoesNotContain("packageSourceCredentials", contents);

        var scratchSettings = Settings.LoadSpecificSettings(scratch, ScratchNuGetConfig.FileName);
        var reloaded = NuGetSourceResolver.Load(scratchSettings);
        Assert.Equal(["internal"], reloaded.Sources.Select(s => s.Name));
        Assert.Equal(configuration.GlobalPackagesFolder, reloaded.GlobalPackagesFolder);
    }

    [Fact]
    public void WorkloadsUseTheScratchConfigurationInsteadOfSourceArguments()
    {
        var configuration = LoadConfiguration(
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="internal" value="https://feed.example/v3/index.json" />
              </packageSources>
            </configuration>
            """);
        var scratch = Directory.CreateDirectory(Path.Combine(_directory, "workloads")).FullName;

        var written = DotNetWorkloadManager.WriteScratchNuGetConfig(
            scratch, configuration, ["https://pkgs.example/dotnet/index.json", "https://api.nuget.org/v3/index.json"]);

        Assert.True(written);
        var scratchSettings = Settings.LoadSpecificSettings(scratch, ScratchNuGetConfig.FileName);
        Assert.Equal(
            ["https://feed.example/v3/index.json", "https://pkgs.example/dotnet/index.json"],
            NuGetSourceResolver.Load(scratchSettings).Sources.Select(s => s.Source));

        var args = DotNetWorkloadManager.BuildInstallArgs("10.0.100", "workload.json", ["wasm-tools"], [], verbose: false, ignoreFailedSources: written);
        Assert.DoesNotContain("--source", args);
        Assert.Contains("--ignore-failed-sources", args);
    }

    [Fact]
    public void WorkloadsKeepTheManifestSourcesWhenTheConfigurationIsUnreadable()
    {
        string[] manifestSources = ["https://pkgs.example/dotnet/index.json", "https://api.nuget.org/v3/index.json"];

        Assert.False(DotNetWorkloadManager.WriteScratchNuGetConfig(_directory, null!, manifestSources));
        Assert.False(File.Exists(Path.Combine(_directory, ScratchNuGetConfig.FileName)));

        var args = DotNetWorkloadManager.BuildInstallArgs("10.0.100", "workload.json", ["wasm-tools"], manifestSources, verbose: false);
        Assert.Equal(2, args.Count(a => a == "--source"));
        Assert.DoesNotContain("--ignore-failed-sources", args);
    }

    [Fact]
    public void ClearedConfigurationDoesNotBringBackThePublicFeedForWorkloads()
    {
        var configuration = LoadConfiguration("<configuration><packageSources><clear /></packageSources></configuration>");
        var scratch = Directory.CreateDirectory(Path.Combine(_directory, "cleared")).FullName;

        var written = DotNetWorkloadManager.WriteScratchNuGetConfig(
            scratch, configuration, ["https://pkgs.example/dotnet/index.json", "https://api.nuget.org/v3/index.json"]);

        Assert.True(written);
        var scratchSettings = Settings.LoadSpecificSettings(scratch, ScratchNuGetConfig.FileName);
        Assert.Equal(["https://pkgs.example/dotnet/index.json"], NuGetSourceResolver.Load(scratchSettings).Sources.Select(s => s.Source));
    }

    [Fact]
    public void ManifestSourcesDisabledByTheConfigurationStayDisabled()
    {
        var configuration = LoadConfiguration(
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="internal" value="https://feed.example/v3/index.json" />
                <add key="preview" value="https://pkgs.example/dotnet/index.json" />
              </packageSources>
              <disabledPackageSources>
                <add key="preview" value="true" />
              </disabledPackageSources>
            </configuration>
            """);

        var merged = NuGetSourceResolver.MergeManifestSources(
            configuration.Sources, ["https://pkgs.example/dotnet/index.json"], configuration.DisabledSources);

        Assert.Equal(["https://feed.example/v3/index.json"], merged.Select(s => s.Source));
    }

    [Fact]
    public void FeedsThatDifferOnlyByPathCaseAreDistinct()
    {
        PackageSource[] configured = [new("https://feed.example/feeds/Release/index.json", "internal")];

        var merged = NuGetSourceResolver.MergeManifestSources(
            configured, ["https://feed.example/feeds/release/index.json", "https://FEED.example:443/feeds/Release/index.json/"]);

        Assert.Equal(
            ["https://feed.example/feeds/Release/index.json", "https://feed.example/feeds/release/index.json"],
            merged.Select(s => s.Source));
    }

    [Fact]
    public void LocalSourcesCompareCaseInsensitivelyOnlyOnWindows()
    {
        PackageSource[] configured = [new(Path.Combine(_directory, "Feed"), "local")];

        var merged = NuGetSourceResolver.MergeManifestSources(configured, [Path.Combine(_directory, "feed")]);

        Assert.Equal(Util.IsWindows ? 1 : 2, merged.Count);
    }

    [Fact]
    public void ConnectionSettingsFollowTheClosestConfigurationThatDefinesTheSource()
    {
        var child = Directory.CreateDirectory(Path.Combine(_directory, "parent", "child")).FullName;
        File.WriteAllText(
            Path.Combine(_directory, "parent", "nuget.config"),
            """
            <configuration><packageSources>
              <add key="uno-check-test-overridden" value="https://old.example/v3/index.json" disableTLSCertificateValidation="true" />
              <add key="uno-check-test-inherited" value="https://inherited.example/v3/index.json" disableTLSCertificateValidation="true" />
            </packageSources></configuration>
            """);
        File.WriteAllText(
            Path.Combine(child, "nuget.config"),
            """
            <configuration><packageSources>
              <add key="uno-check-test-overridden" value="https://new.example/v3/index.json" />
            </packageSources></configuration>
            """);

        var configuration = NuGetSourceResolver.Load(Settings.LoadDefaultSettings(child));

        Assert.False(configuration.ConnectionAttributes.ContainsKey("uno-check-test-overridden"));
        Assert.Equal("true", configuration.ConnectionAttributes["uno-check-test-inherited"]["disableTLSCertificateValidation"]);
    }

    [Fact]
    public void ConnectionSettingsComeFromTheEntryNuGetUsesWhenAKeyIsRepeated()
    {
        var configuration = LoadConfiguration(
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="internal" value="https://first.example/v3/index.json" />
                <add key="internal" value="https://second.example/v3/index.json" disableTLSCertificateValidation="true" />
              </packageSources>
            </configuration>
            """);

        Assert.Equal("https://first.example/v3/index.json", Assert.Single(configuration.Sources).Source);
        Assert.Empty(configuration.ConnectionAttributes);
    }

    [Fact]
    public void ScratchConfigurationIsPrivateToTheCurrentUserOnUnix()
    {
        if (Util.IsWindows)
            return;

        var scratch = Directory.CreateDirectory(Path.Combine(_directory, "private")).FullName;

        var path = ScratchNuGetConfig.Write(scratch, [new PackageSource("https://feed.example/v3/index.json", "internal")]);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(scratch));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void ConnectionSettingsOfConfiguredSourcesAreCarriedToTheScratchConfiguration()
    {
        var configuration = LoadConfiguration(
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="internal" value="https://feed.example/v3/index.json" disableTLSCertificateValidation="true" />
                <add key="mirror" value="http://mirror.example/v3/index.json" allowInsecureConnections="true" />
                <add key="plain" value="https://plain.example/v3/index.json" />
              </packageSources>
            </configuration>
            """);
        var scratch = Directory.CreateDirectory(Path.Combine(_directory, "connection")).FullName;

        var path = ScratchNuGetConfig.Write(scratch, configuration.Sources, connectionAttributes: configuration.ConnectionAttributes);

        var entries = XDocument.Load(path).Root!.Element("packageSources")!.Elements("add").ToDictionary(e => (string)e.Attribute("key")!);
        Assert.Equal("true", (string?)entries["internal"].Attribute("disableTLSCertificateValidation"));
        Assert.Equal("true", (string?)entries["mirror"].Attribute("allowInsecureConnections"));
        Assert.Equal(["key", "value"], entries["plain"].Attributes().Select(a => a.Name.LocalName));
    }

    [Fact]
    public async Task CancellationByTheCallerIsNotSwallowed()
    {
        PackageSource[] sources = [new("https://a.example/index.json", "a")];
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NuGetHelper.GetLatestPackageVersionAsync(
            sources, (_, ct) => Task.FromCanceled<IEnumerable<NuGetVersion>>(ct), TimeSpan.FromSeconds(5), cts.Token));
    }
}
