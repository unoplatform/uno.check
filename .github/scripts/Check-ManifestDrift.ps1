#Requires -Version 7.0

<#
.SYNOPSIS
    Detects when the Uno.Check manifests have fallen behind upstream .NET/tooling releases,
    and when manifest changes on main are waiting for a stable release.

.DESCRIPTION
    The manifests under manifests/ are embedded resources in the Uno.Check package, so a
    manifest change only reaches users once a new package is published. This script watches
    the upstream release trains that drive those manifests and reports drift.

    Checks performed:
      * sdk-servicing    - a newer .NET SDK exists in the pinned feature band
      * sdk-band-move    - a newer feature band exists for the pinned channel
      * workload         - a newer workload manifest package is published
      * url-dead         - a download URL in the manifest no longer resolves
      * release-pending  - manifest commits on main are not in a stable release yet
      * tool-version     - check.toolVersion asks for a tool newer than version.json
      * channel-overlap  - the preview channel offers nothing beyond stable (advisory)
      * xcode / openjdk  - newer releases exist (advisory; these are policy decisions)
      * source-offline   - a data source could not be reached, so its checks are unproven

    Findings are tiered:
      Action   - something must be changed in the repository
      Advisory - worth a look, but not automatically actionable

.PARAMETER SelfTest
    Runs the offline unit tests for the version/band helpers and exits. No network access.

.EXAMPLE
    ./Check-ManifestDrift.ps1 -SelfTest

.EXAMPLE
    ./Check-ManifestDrift.ps1 -JsonOut drift.json -MarkdownOut drift.md
#>

[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,
    [string[]] $ManifestFile,
    [string] $JsonOut,
    [string] $MarkdownOut,
    [int] $ReleaseGraceDays = 7,
    [switch] $SkipAdvisory,
    [switch] $SkipUrlCheck,
    [switch] $SkipGitCheck,
    [switch] $FailOnAction,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'

$script:ReleasesIndexUrl = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json'
$script:XcodeReleasesUrl = 'https://xcodereleases.com/data.json'
$script:FeedCache = @{}
$script:ChannelCache = @{}
$script:Findings = [System.Collections.Generic.List[object]]::new()

#region version helpers

function ConvertTo-SemVer {
    <#  Parses a NuGet/SemVer2 style version into comparable parts. Returns $null when unparsable. #>
    param([string] $Version)

    if ([string]::IsNullOrWhiteSpace($Version)) { return $null }

    $value = $Version.Trim()

    $plus = $value.IndexOf('+')
    if ($plus -ge 0) { $value = $value.Substring(0, $plus) }

    $preRelease = $null
    $dash = $value.IndexOf('-')
    if ($dash -ge 0) {
        $preRelease = $value.Substring($dash + 1)
        $value = $value.Substring(0, $dash)
    }

    $numbers = @(0, 0, 0, 0)
    $parts = $value.Split('.')
    for ($i = 0; $i -lt $parts.Length -and $i -lt 4; $i++) {
        $parsed = 0
        if (-not [int]::TryParse($parts[$i], [ref] $parsed)) { return $null }
        $numbers[$i] = $parsed
    }

    [pscustomobject]@{
        Numbers      = $numbers
        PreRelease   = $preRelease
        IsPreRelease = -not [string]::IsNullOrEmpty($preRelease)
        Original     = $Version
    }
}

function Compare-SemVer {
    <#  Returns -1/0/1, or $null when either side is unparsable. #>
    param([string] $Left, [string] $Right)

    $a = ConvertTo-SemVer $Left
    $b = ConvertTo-SemVer $Right
    if ($null -eq $a -or $null -eq $b) { return $null }

    for ($i = 0; $i -lt 4; $i++) {
        if ($a.Numbers[$i] -ne $b.Numbers[$i]) {
            return [Math]::Sign($a.Numbers[$i] - $b.Numbers[$i])
        }
    }

    if (-not $a.IsPreRelease -and -not $b.IsPreRelease) { return 0 }
    if (-not $a.IsPreRelease) { return 1 }
    if (-not $b.IsPreRelease) { return -1 }

    $left = $a.PreRelease.Split('.')
    $right = $b.PreRelease.Split('.')
    $max = [Math]::Max($left.Length, $right.Length)

    for ($i = 0; $i -lt $max; $i++) {
        if ($i -ge $left.Length) { return -1 }
        if ($i -ge $right.Length) { return 1 }

        $x = $left[$i]
        $y = $right[$i]
        $xn = 0
        $yn = 0
        $xIsNumeric = [int]::TryParse($x, [ref] $xn)
        $yIsNumeric = [int]::TryParse($y, [ref] $yn)

        if ($xIsNumeric -and $yIsNumeric) {
            if ($xn -ne $yn) { return [Math]::Sign($xn - $yn) }
            continue
        }

        # Numeric identifiers always have lower precedence than alphanumeric ones.
        if ($xIsNumeric) { return -1 }
        if ($yIsNumeric) { return 1 }

        $ordinal = [string]::CompareOrdinal($x, $y)
        if ($ordinal -ne 0) { return [Math]::Sign($ordinal) }
    }

    return 0
}

function Get-FeatureBand {
    <#  10.0.201 -> 10.0.200 (the numeric band), used to group SDK releases. #>
    param([string] $SdkVersion)

    $semver = ConvertTo-SemVer $SdkVersion
    if ($null -eq $semver) { return $null }

    $band = [Math]::Floor($semver.Numbers[2] / 100) * 100
    '{0}.{1}.{2}' -f $semver.Numbers[0], $semver.Numbers[1], $band
}

function Format-FeatureBand {
    param([string] $SdkVersion)

    $band = Get-FeatureBand $SdkVersion
    if (-not $band) { return '?' }
    $band.Substring(0, $band.Length - 2) + 'xx'
}

function Get-ChannelVersion {
    param([string] $SdkVersion)

    $semver = ConvertTo-SemVer $SdkVersion
    if ($null -eq $semver) { return $null }
    '{0}.{1}' -f $semver.Numbers[0], $semver.Numbers[1]
}

function Get-Prop {
    <#  StrictMode-safe property access: a manifest without an optional node must degrade
        to "nothing to check", never crash the checker (a crashed checker reads as no drift). #>
    param($Object, [string] $Name, $Default = $null)

    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    $property.Value
}

function Get-LatestVersion {
    param([string[]] $Versions, [bool] $AllowPreRelease)

    $best = $null
    foreach ($version in $Versions) {
        $semver = ConvertTo-SemVer $version
        if ($null -eq $semver) { continue }
        if ($semver.IsPreRelease -and -not $AllowPreRelease) { continue }
        if ($null -eq $best -or (Compare-SemVer $version $best) -gt 0) { $best = $version }
    }
    $best
}

#endregion

#region findings

function Add-Finding {
    param(
        [ValidateSet('Action', 'Advisory')] [string] $Tier,
        [string] $Kind,
        [string] $Manifest,
        [string] $Subject,
        [string] $Current,
        [string] $Latest,
        [string] $Detail
    )

    $script:Findings.Add([pscustomobject]@{
            Tier     = $Tier
            Kind     = $Kind
            Manifest = $Manifest
            Subject  = $Subject
            Current  = $Current
            Latest   = $Latest
            Detail   = $Detail
        })
}

#endregion

#region self test

function Invoke-SelfTest {
    $failures = [System.Collections.Generic.List[string]]::new()

    function Assert-Equal {
        param($Expected, $Actual, [string] $Because)
        if ("$Expected" -ne "$Actual") {
            $failures.Add("$Because -- expected '$Expected', got '$Actual'")
        }
    }

    Assert-Equal 1 (Compare-SemVer '36.1.69' '36.1.43') 'newer patch wins'
    Assert-Equal -1 (Compare-SemVer '26.2.10217' '26.5.10301') 'older minor loses'
    Assert-Equal 0 (Compare-SemVer '10.0.201' '10.0.201.0') 'missing 4th part is zero'
    Assert-Equal 1 (Compare-SemVer '11.0.100' '11.0.100-preview.6') 'release beats prerelease'
    Assert-Equal -1 (Compare-SemVer '11.0.100-preview.6' '11.0.100-preview.7') 'prerelease ordinal ordering'
    Assert-Equal 1 (Compare-SemVer '11.0.100-preview.6.26359.118' '11.0.100-preview.6.26359.117') 'prerelease build ordering'
    Assert-Equal -1 (Compare-SemVer '1.0.0-alpha.1' '1.0.0-alpha.beta') 'numeric identifier sorts below alphanumeric'
    Assert-Equal $null (Compare-SemVer 'not-a-version' '1.0.0') 'unparsable returns null'

    Assert-Equal '10.0.200' (Get-FeatureBand '10.0.201') 'band of 10.0.201'
    Assert-Equal '10.0.100' (Get-FeatureBand '10.0.100') 'band of 10.0.100'
    Assert-Equal '11.0.100' (Get-FeatureBand '11.0.100-preview.6.26359.118') 'band of a preview sdk'
    Assert-Equal '10.0.2xx' (Format-FeatureBand '10.0.201') 'formatted band'
    Assert-Equal '10.0' (Get-ChannelVersion '10.0.302') 'channel of 10.0.302'

    Assert-Equal '36.1.69' (Get-LatestVersion @('36.1.43', '36.1.69', '36.1.53') $false) 'latest stable'
    Assert-Equal '36.1.69' (Get-LatestVersion @('36.1.69', '37.0.0-preview.1') $false) 'prerelease excluded'
    Assert-Equal '37.0.0-preview.1' (Get-LatestVersion @('36.1.69', '37.0.0-preview.1') $true) 'prerelease included'

    $variables = @{ 'IOS_SDK_VERSION' = '26.2.10217/10.0.100'; 'DOTNET_SDK_VERSION' = '10.0.201' }
    Assert-Equal '26.2.10217/10.0.100' (Resolve-ManifestVariable '$(IOS_SDK_VERSION)' $variables) 'variable substitution'
    Assert-Equal 'sdk-10.0.201.exe' (Resolve-ManifestVariable 'sdk-$(DOTNET_SDK_VERSION).exe' $variables) 'embedded substitution'
    Assert-Equal '$(UNKNOWN)' (Resolve-ManifestVariable '$(UNKNOWN)' $variables) 'unknown variable is left alone'

    Assert-Equal '26.2.10217' (Split-WorkloadVersion '26.2.10217/10.0.100').Version 'workload version part'
    Assert-Equal '10.0.100' (Split-WorkloadVersion '26.2.10217/10.0.100').Band 'workload band part'
    Assert-Equal '' (Split-WorkloadVersion '36.1.43').Band 'workload without band'

    $shape = [pscustomobject]@{ present = 'value'; nullish = $null }
    Assert-Equal 'value' (Get-Prop $shape 'present') 'Get-Prop existing property'
    Assert-Equal 'fallback' (Get-Prop $shape 'absent' 'fallback') 'Get-Prop missing property'
    Assert-Equal 'fallback' (Get-Prop $shape 'nullish' 'fallback') 'Get-Prop null-valued property'
    Assert-Equal 'fallback' (Get-Prop $null 'anything' 'fallback') 'Get-Prop null object'

    if ($failures.Count -gt 0) {
        Write-Host "Self-test FAILED ($($failures.Count)):" -ForegroundColor Red
        $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        return $false
    }

    Write-Host 'Self-test passed.' -ForegroundColor Green
    return $true
}

#endregion

#region manifest parsing

function Resolve-ManifestVariable {
    param([string] $Value, [hashtable] $Variables)

    if ([string]::IsNullOrEmpty($Value)) { return $Value }

    $result = $Value
    foreach ($key in $Variables.Keys) {
        $result = $result.Replace('$(' + $key + ')', [string] $Variables[$key])
    }
    $result
}

function Split-WorkloadVersion {
    <#  '26.2.10217/10.0.100' -> Version + Band. The band half mirrors the packageId suffix. #>
    param([string] $Value)

    $parts = ($Value ?? '').Split('/')
    [pscustomobject]@{
        Version = $parts[0]
        Band    = if ($parts.Length -gt 1) { $parts[1] } else { '' }
    }
}

function Read-CheckManifest {
    param([string] $Path)

    $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $check = Get-Prop $json 'check'
    if ($null -eq $check) {
        throw "'$Path' has no top-level 'check' node; it is not an Uno.Check manifest."
    }

    $variables = @{}
    $variableNode = Get-Prop $check 'variables'
    if ($variableNode) {
        foreach ($property in $variableNode.PSObject.Properties) {
            $variables[$property.Name] = [string] $property.Value
        }
    }

    [pscustomobject]@{
        Name      = [IO.Path]::GetFileNameWithoutExtension($Path) -replace '\.manifest$', ''
        Path      = $Path
        Json      = $json
        Check     = $check
        Variables = $variables
    }
}

function Get-VariableNameFor {
    <#  Maps a raw manifest value back to the variable that produced it, for actionable reporting. #>
    param([string] $RawValue, [hashtable] $Variables, [string] $Fallback)

    if ($RawValue -match '^\$\(([^)]+)\)$') { return $Matches[1] }

    foreach ($key in $Variables.Keys) {
        if ($Variables[$key] -eq $RawValue) { return $key }
    }

    $Fallback
}

#endregion

#region remote sources

function Invoke-Json {
    param([string] $Uri, [int] $TimeoutSec = 60)

    Invoke-RestMethod -Uri $Uri -TimeoutSec $TimeoutSec -MaximumRetryCount 2 -RetryIntervalSec 5 -Headers @{ 'User-Agent' = 'uno-check-manifest-drift' }
}

function Get-FlatContainerBase {
    param([string] $SourceUrl)

    if ($script:FeedCache.ContainsKey($SourceUrl)) { return $script:FeedCache[$SourceUrl] }

    $base = $null
    try {
        if ($SourceUrl -match 'api\.nuget\.org') {
            $base = 'https://api.nuget.org/v3-flatcontainer/'
        }
        else {
            $index = Invoke-Json $SourceUrl
            $resource = $index.resources | Where-Object { $_.'@type' -like 'PackageBaseAddress/*' } | Select-Object -First 1
            if ($resource) { $base = [string] $resource.'@id' }
        }
    }
    catch {
        Write-Warning "Could not resolve NuGet feed '$SourceUrl': $($_.Exception.Message)"
    }

    if ($base -and -not $base.EndsWith('/')) { $base += '/' }
    $script:FeedCache[$SourceUrl] = $base
    $base
}

function Get-PackageVersion {
    <#  Union of versions across every configured source; a 404 on one feed is expected.
        Tracks which source carried each version, so a build that only exists on a dotnet
        build feed is not reported as if Microsoft had shipped it. #>
    param([string] $PackageId, [string[]] $Sources)

    $sourcesByVersion = @{}
    $reachable = $false

    foreach ($source in $Sources) {
        $base = Get-FlatContainerBase $source
        if (-not $base) { continue }

        $uri = "$base$($PackageId.ToLowerInvariant())/index.json"
        try {
            $response = Invoke-Json $uri
            $reachable = $true
            foreach ($version in $response.versions) {
                $key = [string] $version
                if (-not $sourcesByVersion.ContainsKey($key)) {
                    $sourcesByVersion[$key] = [System.Collections.Generic.List[string]]::new()
                }
                $sourcesByVersion[$key].Add($source)
            }
        }
        catch {
            # A 404 is a real answer from a reachable feed (the package is simply not on this
            # source). Connection-level failures - DNS, TLS, timeout - throw exception types
            # with no Response property at all, so this must go through Get-Prop: under
            # StrictMode a bare $_.Exception.Response would terminate inside the catch, and a
            # crashed checker reads as "no drift".
            $status = Get-Prop (Get-Prop $_.Exception 'Response') 'StatusCode'
            if ($null -ne $status -and [int] $status -eq 404) { $reachable = $true }
        }
    }

    [pscustomobject]@{
        Versions         = @($sourcesByVersion.Keys)
        SourcesByVersion = $sourcesByVersion
        Reachable        = $reachable
    }
}

function Get-DotNetChannel {
    param([string] $ChannelVersion)

    if ($script:ChannelCache.ContainsKey($ChannelVersion)) { return $script:ChannelCache[$ChannelVersion] }

    $result = $null
    try {
        $index = Invoke-Json $script:ReleasesIndexUrl
        $channel = $index.'releases-index' | Where-Object { $_.'channel-version' -eq $ChannelVersion } | Select-Object -First 1

        if ($channel) {
            $releases = Invoke-Json $channel.'releases.json'
            $sdkVersions = [System.Collections.Generic.List[string]]::new()

            foreach ($release in $releases.releases) {
                $candidates = @()
                if ($release.PSObject.Properties.Name -contains 'sdks' -and $release.sdks) { $candidates += $release.sdks }
                if ($release.PSObject.Properties.Name -contains 'sdk' -and $release.sdk) { $candidates += $release.sdk }

                foreach ($sdk in $candidates) {
                    if ($sdk.version -and -not $sdkVersions.Contains([string] $sdk.version)) {
                        $sdkVersions.Add([string] $sdk.version)
                    }
                }
            }

            $result = [pscustomobject]@{
                ChannelVersion = $ChannelVersion
                SupportPhase   = [string] $channel.'support-phase'
                LatestSdk      = [string] $channel.'latest-sdk'
                SdkVersions    = $sdkVersions
            }
        }
    }
    catch {
        Write-Warning "Could not read .NET release metadata for channel $ChannelVersion : $($_.Exception.Message)"
    }

    $script:ChannelCache[$ChannelVersion] = $result
    $result
}

function Test-UrlAlive {
    <#  Returns 'alive', 'dead' or 'offline'.

        'dead' is reserved for a definite answer from a working server (a non-429 4xx).
        Anything else - DNS/TLS failure, timeout, 429, 5xx - is 'offline': unproven, not
        broken. This distinction matters because url-dead is an Action-tier finding that
        tells maintainers "users will fail to install", so a flaky runner must never
        produce one. Callers must compare against 'alive' explicitly: every return value
        here is a non-empty string, and so truthy. #>
    param([string] $Uri)

    $definite = $false

    foreach ($method in @('Head', 'Get')) {
        try {
            $arguments = @{
                Uri                = $Uri
                Method             = $method
                TimeoutSec         = 30
                MaximumRedirection = 10
                MaximumRetryCount  = 2
                RetryIntervalSec   = 5
                SkipHttpErrorCheck = $true
                ErrorAction        = 'Stop'
            }
            # Ask for a single byte so a GET fallback never downloads an installer.
            if ($method -eq 'Get') { $arguments['Headers'] = @{ Range = 'bytes=0-0' } }

            $response = Invoke-WebRequest @arguments
            if ($response.StatusCode -lt 400) { return 'alive' }

            # 429 and 5xx are the server having a bad day, not a missing file. A HEAD may
            # also be refused (405) where a GET succeeds, so keep trying the next method.
            if ($response.StatusCode -ne 429 -and $response.StatusCode -lt 500) { $definite = $true }
        }
        catch {
            continue
        }
    }

    if ($definite) { return 'dead' }
    return 'offline'
}

#endregion

#region checks

function Get-ManifestSdk {
    param($Manifest)
    @(Get-Prop (Get-Prop $Manifest.Check 'dotnet') 'sdks' @())
}

function Test-SdkVersion {
    param($Manifest)

    foreach ($sdk in (Get-ManifestSdk $Manifest)) {
        $pinnedRaw = [string] $sdk.version
        $pinned = Resolve-ManifestVariable $pinnedRaw $Manifest.Variables
        $variable = Get-VariableNameFor $pinnedRaw $Manifest.Variables 'DOTNET_SDK_VERSION'

        $channelVersion = Get-ChannelVersion $pinned
        if (-not $channelVersion) {
            Add-Finding -Tier Advisory -Kind 'manifest-parse' -Manifest $Manifest.Name -Subject $variable `
                -Current $pinned -Detail 'Pinned SDK version could not be parsed.'
            continue
        }

        $channel = Get-DotNetChannel $channelVersion
        if (-not $channel) {
            Add-Finding -Tier Advisory -Kind 'source-offline' -Manifest $Manifest.Name -Subject $variable `
                -Current $pinned -Detail "Release metadata for channel $channelVersion was unreachable; SDK drift is unproven for this run."
            continue
        }

        $allowPreRelease = (ConvertTo-SemVer $pinned).IsPreRelease
        $pinnedBand = Get-FeatureBand $pinned

        $inBand = Get-LatestVersion (($channel.SdkVersions | Where-Object { (Get-FeatureBand $_) -eq $pinnedBand })) $allowPreRelease
        if ($inBand -and (Compare-SemVer $inBand $pinned) -gt 0) {
            Add-Finding -Tier Action -Kind 'sdk-servicing' -Manifest $Manifest.Name -Subject $variable `
                -Current $pinned -Latest $inBand `
                -Detail "Newer servicing release in band $(Format-FeatureBand $pinned)."
        }

        $overall = Get-LatestVersion $channel.SdkVersions $allowPreRelease
        if ($overall -and (Get-FeatureBand $overall) -ne $pinnedBand -and (Compare-SemVer $overall $pinned) -gt 0) {
            Add-Finding -Tier Advisory -Kind 'sdk-band-move' -Manifest $Manifest.Name -Subject $variable `
                -Current $pinned -Latest $overall `
                -Detail "Feature band $(Format-FeatureBand $overall) is available (pinned band is $(Format-FeatureBand $pinned)). Moving bands is a deliberate call: re-verify the workload packageId suffixes on a clean machine."
        }
    }
}

function Test-Workload {
    param($Manifest)

    $seen = @{}

    foreach ($sdk in (Get-ManifestSdk $Manifest)) {
        $sources = @(Get-Prop $sdk 'packageSources' @())
        if ($sources.Count -eq 0) { $sources = @('https://api.nuget.org/v3/index.json') }

        foreach ($workload in @(Get-Prop $sdk 'workloads' @())) {
            $packageId = [string] $workload.packageId
            $pinnedRaw = [string] $workload.version
            $variable = Get-VariableNameFor $pinnedRaw $Manifest.Variables $workload.workloadId

            # maui and maui-android share one manifest package; report the variable once.
            $key = "$packageId|$variable"
            if ($seen.ContainsKey($key)) { continue }
            $seen[$key] = $true

            $pinned = Split-WorkloadVersion (Resolve-ManifestVariable $pinnedRaw $Manifest.Variables)
            $pinnedSemVer = ConvertTo-SemVer $pinned.Version
            if ($null -eq $pinnedSemVer) {
                Add-Finding -Tier Advisory -Kind 'manifest-parse' -Manifest $Manifest.Name -Subject $variable `
                    -Current $pinned.Version -Detail 'Pinned workload version could not be parsed (unresolved variable?), so this workload is not being drift-checked.'
                continue
            }

            $lookup = Get-PackageVersion -PackageId $packageId -Sources $sources

            if (-not $lookup.Reachable) {
                Add-Finding -Tier Advisory -Kind 'source-offline' -Manifest $Manifest.Name -Subject $variable `
                    -Current $pinned.Version -Detail "No configured feed answered for $packageId; workload drift is unproven for this run."
                continue
            }

            if ($lookup.Versions.Count -eq 0) {
                # packageId is only used in the tool's diagnostic output today (DotNetWorkloadsCheckup),
                # so a stale id does not break installs - but it does blind this drift check.
                Add-Finding -Tier Action -Kind 'workload-unknown-package' -Manifest $Manifest.Name -Subject $variable `
                    -Current $pinned.Version `
                    -Detail "No such package as $packageId on the configured feeds, so this workload's version is NOT being drift-checked. The id is only used in the tool's error text today, so installs still work, but it should be corrected."
                continue
            }

            $allowPreRelease = $pinnedSemVer.IsPreRelease
            $latest = Get-LatestVersion $lookup.Versions $allowPreRelease
            if (-not $latest) { continue }

            $comparison = Compare-SemVer $latest $pinned.Version
            if ($null -ne $comparison -and $comparison -gt 0) {
                $suggested = if ($pinned.Band) { "$latest/$($pinned.Band)" } else { $latest }
                $foundOn = @($lookup.SourcesByVersion[$latest])
                $onNuGetOrg = @($foundOn | Where-Object { $_ -match 'api\.nuget\.org' }).Count -gt 0

                # A stable channel should only chase versions Microsoft has actually shipped publicly.
                # Preview channels legitimately consume the dotnet build feeds first.
                $tier = 'Action'
                $note = ''
                if (-not $onNuGetOrg -and -not $allowPreRelease) {
                    $tier = 'Advisory'
                    $note = ' Not on nuget.org yet (build feed only) - may be an unreleased build.'
                }

                Add-Finding -Tier $tier -Kind 'workload' -Manifest $Manifest.Name -Subject $variable `
                    -Current (Resolve-ManifestVariable $pinnedRaw $Manifest.Variables) -Latest $suggested `
                    -Detail "$packageId published $latest.$note"
            }
        }
    }
}

function Test-ManifestUrl {
    param($Manifest)

    $urls = [System.Collections.Generic.List[string]]::new()

    $openJdkUrls = Get-Prop (Get-Prop $Manifest.Check 'openjdk') 'urls'
    if ($openJdkUrls) {
        foreach ($property in $openJdkUrls.PSObject.Properties) { $urls.Add([string] $property.Value) }
    }

    foreach ($sdk in (Get-ManifestSdk $Manifest)) {
        $sdkUrls = Get-Prop $sdk 'urls'
        if ($sdkUrls) {
            foreach ($property in $sdkUrls.PSObject.Properties) { $urls.Add([string] $property.Value) }
        }
    }

    foreach ($url in ($urls | Select-Object -Unique)) {
        $resolved = Resolve-ManifestVariable $url $Manifest.Variables
        if ($resolved -match '\$\(') { continue }

        switch (Test-UrlAlive $resolved) {
            'dead' {
                Add-Finding -Tier Action -Kind 'url-dead' -Manifest $Manifest.Name -Subject 'download url' `
                    -Current $resolved -Detail 'URL did not resolve. Users will fail to install this component.'
            }
            'offline' {
                Add-Finding -Tier Advisory -Kind 'source-offline' -Manifest $Manifest.Name -Subject 'download url' `
                    -Current $resolved -Detail 'URL could not be probed (timeout, connection failure or server error); its liveness is unproven for this run.'
            }
        }
    }
}

function Test-ToolVersion {
    param($Manifest, [string] $RepositoryVersion)

    $toolVersion = [string] (Get-Prop $Manifest.Check 'toolVersion' '')

    if ([string]::IsNullOrWhiteSpace($toolVersion)) {
        Add-Finding -Tier Action -Kind 'tool-version' -Manifest $Manifest.Name -Subject 'check.toolVersion' `
            -Detail 'Manifest does not declare check.toolVersion; the tool aborts on this manifest under --ci.'
        return
    }

    if (-not $RepositoryVersion) { return }

    if ((Compare-SemVer $toolVersion $RepositoryVersion) -gt 0) {
        Add-Finding -Tier Action -Kind 'tool-version' -Manifest $Manifest.Name -Subject 'check.toolVersion' `
            -Current $toolVersion -Latest $RepositoryVersion `
            -Detail "Manifest requires a tool version newer than version.json ($RepositoryVersion). Every user on this manifest gets an update warning that no release can satisfy."
    }
}

function Test-ChannelOverlap {
    param($Stable, $Preview)

    if (-not $Stable -or -not $Preview) { return }

    $differences = $Preview.Variables.Keys | Where-Object {
        -not $Stable.Variables.ContainsKey($_) -or $Stable.Variables[$_] -ne $Preview.Variables[$_]
    }

    if (-not $differences) {
        Add-Finding -Tier Advisory -Kind 'channel-overlap' -Manifest $Preview.Name -Subject 'variables' `
            -Detail 'The preview manifest pins exactly the same versions as stable, so --pre currently gives users nothing extra. Expected while the current major is GA with no in-band preview; otherwise the preview channel needs a bump.'
    }
}

function Test-ReleasePending {
    param([string] $Root, [int] $GraceDays)

    Push-Location $Root
    try {
        $tags = @(git tag --list --sort=-v:refname 2>$null) | Where-Object { $_ -match '^\d+\.\d+(\.\d+)?$' }
        if (-not $tags -or $tags.Count -eq 0) {
            Add-Finding -Tier Advisory -Kind 'source-offline' -Manifest 'repository' -Subject 'release tags' `
                -Detail 'No stable release tags found. Fetch tags (fetch-depth: 0) for the release-pending check to work.'
            return
        }

        $lastTag = $tags[0]

        $head = 'HEAD'
        if (git rev-parse --verify --quiet origin/main 2>$null) { $head = 'origin/main' }

        $log = @(git log --format='%H%x09%cI%x09%s' "$lastTag..$head" -- manifests/ 2>$null)
        if (-not $log -or $log.Count -eq 0) { return }

        $oldest = $log[-1].Split("`t")
        $oldestDate = [datetimeoffset]::Parse($oldest[1])
        $age = [int] ([datetimeoffset]::UtcNow - $oldestDate).TotalDays
        $tier = if ($age -ge $GraceDays) { 'Action' } else { 'Advisory' }

        $subjects = ($log | ForEach-Object { ($_ -split "`t")[2] } | Select-Object -First 5) -join '; '

        Add-Finding -Tier $tier -Kind 'release-pending' -Manifest 'repository' -Subject "since $lastTag" `
            -Current "$($log.Count) manifest commit(s), oldest $age day(s) old" -Latest $lastTag `
            -Detail "Manifest changes are on $head but not in a stable release. Users still get the versions from $lastTag until a release/stable branch is cut. Commits: $subjects"
    }
    finally {
        Pop-Location
    }
}

function Test-Advisory {
    param($Manifests)

    # Xcode: the manifest minimum is a policy decision, so this is never an Action.
    try {
        $xcode = Invoke-Json $script:XcodeReleasesUrl
        # Entries are newest-first; the first one whose release object says release=true is the latest GA.
        $latestRelease = $xcode | Where-Object { (Get-Prop (Get-Prop (Get-Prop $_ 'version') 'release') 'release' $false) -eq $true } | Select-Object -First 1
        if ($latestRelease) {
            $latestXcode = [string] $latestRelease.version.number
            foreach ($manifest in $Manifests) {
                $minimum = [string] (Get-Prop (Get-Prop $manifest.Check 'xcode') 'minimumVersion' '')
                if (-not $minimum) { continue }
                if ((Compare-SemVer $latestXcode $minimum) -gt 0) {
                    Add-Finding -Tier Advisory -Kind 'xcode' -Manifest $manifest.Name -Subject 'xcode.minimumVersion' `
                        -Current $minimum -Latest $latestXcode `
                        -Detail 'A newer Xcode GA exists. Only raise the minimum once the matching iOS/tvOS/MacCatalyst bindings are pinned, or macOS users are told to upgrade for workloads that do not exist yet.'
                }
            }
        }
    }
    catch {
        Write-Warning "Xcode release data unavailable: $($_.Exception.Message)"
    }

    # OpenJDK: track the GA line of whatever major each manifest pins (17 today, whatever
    # tomorrow), and only suggest a version Microsoft has actually published.
    $jdkLatestByMajor = @{}
    foreach ($manifest in $Manifests) {
        if (-not $manifest.Variables.ContainsKey('OPENJDK_VERSION')) { continue }
        $current = $manifest.Variables['OPENJDK_VERSION']
        $currentSemVer = ConvertTo-SemVer $current
        if ($null -eq $currentSemVer) { continue }

        $major = $currentSemVer.Numbers[0]
        if (-not $jdkLatestByMajor.ContainsKey($major)) {
            $jdkLatestByMajor[$major] = $null
            try {
                $uri = 'https://api.adoptium.net/v3/info/release_versions?version=%5B{0}%2C{1}%29&release_type=ga&page_size=1&sort_order=DESC' -f $major, ($major + 1)
                $adoptium = Invoke-Json $uri
                $jdkLatestByMajor[$major] = [string] $adoptium.versions[0].semver -replace '\+.*$', ''
            }
            catch {
                Write-Warning "OpenJDK $major release data unavailable: $($_.Exception.Message)"
            }
        }

        $latestJdk = $jdkLatestByMajor[$major]
        if (-not $latestJdk -or (Compare-SemVer $latestJdk $current) -le 0) { continue }

        $probe = "https://aka.ms/download-jdk/microsoft-jdk-$latestJdk-windows-x64.msi"
        if ((Test-UrlAlive $probe) -eq 'alive') {
            Add-Finding -Tier Advisory -Kind 'openjdk' -Manifest $manifest.Name -Subject 'OPENJDK_VERSION' `
                -Current $current -Latest $latestJdk -Detail 'Microsoft has published this JDK build.'
        }
    }
}

#endregion

#region reporting

function Format-FindingTable {
    param($Findings)

    $lines = @('| Manifest | What | Pinned | Available | Notes |', '| --- | --- | --- | --- | --- |')
    foreach ($finding in $Findings) {
        $lines += '| {0} | `{1}` ({2}) | {3} | {4} | {5} |' -f `
            $finding.Manifest,
        $finding.Subject,
        $finding.Kind,
        ($(if ($finding.Current) { '`' + $finding.Current + '`' } else { '—' })),
        ($(if ($finding.Latest) { '`' + $finding.Latest + '`' } else { '—' })),
        ($finding.Detail -replace '\|', '\|')
    }
    $lines -join "`n"
}

function New-Report {
    param($Findings, [string] $RepositoryVersion)

    $actions = @($Findings | Where-Object Tier -EQ 'Action')
    $advisories = @($Findings | Where-Object Tier -EQ 'Advisory')

    # A source we could not read, or a pin we could not parse, means some subjects went
    # unchecked this run. "No actions" then means "nothing proven", not "nothing wrong" -
    # Sync-DriftIssue uses this to refuse to declare the drift resolved.
    $degraded = @($Findings | Where-Object { $_.Kind -in @('source-offline', 'manifest-parse') })

    $body = [System.Text.StringBuilder]::new()

    if ($actions.Count -eq 0 -and $degraded.Count -gt 0) {
        [void] $body.AppendLine("No action items, but $($degraded.Count) check(s) could not be completed this run (see Advisory below). Drift is unproven, not absent.")
    }
    elseif ($actions.Count -eq 0) {
        [void] $body.AppendLine('No manifest drift detected. Every pinned .NET SDK, workload manifest and download URL is current.')
    }
    else {
        [void] $body.AppendLine("### Action required ($($actions.Count))")
        [void] $body.AppendLine()
        [void] $body.AppendLine((Format-FindingTable $actions))
        [void] $body.AppendLine()
        [void] $body.AppendLine('**How to act on this**')
        [void] $body.AppendLine()
        [void] $body.AppendLine('1. Do not copy the "Available" column straight into the manifest. It is a drift signal, not a validated set.')
        [void] $body.AppendLine('2. Follow `AGENTS.md` section 5: install the target SDK into a clean folder, `dotnet workload install ios android maui catalyst tvos wasm-tools`, then `dotnet workload list` and transcribe the **Manifest Version** column verbatim (the `version/band` form).')
        [void] $body.AppendLine('3. Update the `check.variables` block of the affected manifest(s) and open a PR. The CI matrix validates all three manifests on Windows, macOS and Linux.')
        [void] $body.AppendLine('4. Merging to `main` only publishes a `-dev` prerelease. Users get nothing until a `release/stable/X.Y` branch is cut.')
    }

    if ($advisories.Count -gt 0) {
        [void] $body.AppendLine()
        [void] $body.AppendLine("### Advisory ($($advisories.Count))")
        [void] $body.AppendLine()
        [void] $body.AppendLine((Format-FindingTable $advisories))
    }

    [void] $body.AppendLine()
    [void] $body.AppendLine('---')
    [void] $body.AppendLine("Repository version: ``$RepositoryVersion`` · Checked $([datetimeoffset]::UtcNow.ToString('yyyy-MM-dd HH:mm')) UTC")

    $fingerprintSource = ($actions | ForEach-Object { '{0}|{1}|{2}|{3}|{4}' -f $_.Kind, $_.Manifest, $_.Subject, $_.Current, $_.Latest } | Sort-Object) -join "`n"
    $hash = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($fingerprintSource))
    $fingerprint = ([System.BitConverter]::ToString($hash) -replace '-', '').Substring(0, 16).ToLowerInvariant()

    [pscustomobject]@{
        HasAction       = $actions.Count -gt 0
        ActionCount     = $actions.Count
        AdvisoryCount   = $advisories.Count
        SourcesDegraded = $degraded.Count -gt 0
        DegradedCount   = $degraded.Count
        Fingerprint     = $fingerprint
        Findings        = $Findings
        Markdown        = $body.ToString()
        GeneratedUtc    = [datetimeoffset]::UtcNow.ToString('o')
    }
}

#endregion

# ---------------------------------------------------------------------------

if ($SelfTest) {
    exit ($(if (Invoke-SelfTest) { 0 } else { 1 }))
}

if (-not (Invoke-SelfTest)) {
    Write-Error 'Self-test failed; refusing to report drift with broken version comparison.'
    exit 1
}

if (-not $ManifestFile -or $ManifestFile.Count -eq 0) {
    $ManifestFile = @(Get-ChildItem -Path (Join-Path $RepositoryRoot 'manifests') -Filter '*.manifest.json' | Sort-Object Name | Select-Object -ExpandProperty FullName)
}

$repositoryVersion = $null
$versionJsonPath = Join-Path $RepositoryRoot 'version.json'
if (Test-Path $versionJsonPath) {
    $versionJson = Get-Content -LiteralPath $versionJsonPath -Raw | ConvertFrom-Json
    $repositoryVersion = ([string] $versionJson.version) -replace '-.*$', ''
}

$manifests = @()
foreach ($path in $ManifestFile) {
    Write-Host "Reading $path"
    $manifests += Read-CheckManifest $path
}

foreach ($manifest in $manifests) {
    Write-Host "Checking $($manifest.Name)..."
    Test-SdkVersion $manifest
    Test-Workload $manifest
    Test-ToolVersion $manifest $repositoryVersion
    if (-not $SkipUrlCheck) { Test-ManifestUrl $manifest }
}

$stable = $manifests | Where-Object { $_.Name -eq 'uno.ui' } | Select-Object -First 1
$preview = $manifests | Where-Object { $_.Name -eq 'uno.ui-preview' } | Select-Object -First 1
if (-not $SkipAdvisory) {
    Test-ChannelOverlap $stable $preview
    Test-Advisory $manifests
}

if (-not $SkipGitCheck) { Test-ReleasePending $RepositoryRoot $ReleaseGraceDays }

$report = New-Report $script:Findings $repositoryVersion

Write-Host ''
Write-Host "Action items: $($report.ActionCount) | Advisories: $($report.AdvisoryCount) | Unproven: $($report.DegradedCount) | Fingerprint: $($report.Fingerprint)"
Write-Host ''
Write-Host $report.Markdown

if ($JsonOut) {
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $JsonOut -Encoding utf8
    Write-Host "Wrote $JsonOut"
}

if ($MarkdownOut) {
    $report.Markdown | Set-Content -LiteralPath $MarkdownOut -Encoding utf8
    Write-Host "Wrote $MarkdownOut"
}

if ($env:GITHUB_STEP_SUMMARY) {
    "## Manifest drift`n`n$($report.Markdown)" | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

if ($env:GITHUB_OUTPUT) {
    "has_action=$($report.HasAction.ToString().ToLowerInvariant())" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
    "action_count=$($report.ActionCount)" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
    "fingerprint=$($report.Fingerprint)" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
}

if ($FailOnAction -and $report.HasAction) { exit 2 }
exit 0
