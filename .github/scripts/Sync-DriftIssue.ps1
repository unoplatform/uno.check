#Requires -Version 7.0

<#
.SYNOPSIS
    Turns a manifest drift report into a single, self-maintaining GitHub tracking issue.

.DESCRIPTION
    Idempotent by design, so a daily schedule does not create noise:
      * no drift, no issue      -> nothing happens
      * drift, no issue         -> opens one
      * drift, issue exists     -> refreshes the body in place; comments ONLY when the
                                   set of action items actually changed, listing what is
                                   new and what went away
      * drift resolved          -> comments and closes the issue

    The previous state is carried in a base64 HTML comment inside the issue body, so no
    external storage is needed.

.PARAMETER DryRun
    Prints what would happen and writes the rendered body to disk. Makes no gh calls.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ReportPath,
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [string] $Label = 'manifest-drift',
    [string] $Title = 'Manifest drift: pinned .NET SDK / workloads are behind upstream',
    [string[]] $Assignee,
    [string] $RunUrl,
    [string] $BodyOut,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$marker = '<!-- uno-check-manifest-drift -->'

function Get-ActionKey {
    param($Finding)
    '{0}|{1}|{2}|{3}|{4}' -f $Finding.Kind, $Finding.Manifest, $Finding.Subject, $Finding.Current, $Finding.Latest
}

function ConvertTo-StateComment {
    param([string[]] $Keys)
    $json = ($Keys | Sort-Object) | ConvertTo-Json -Compress -AsArray
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
    "<!-- state:$encoded -->"
}

function ConvertFrom-StateComment {
    param([string] $Body)
    if (-not $Body -or $Body -notmatch '<!-- state:([A-Za-z0-9+/=]+) -->') { return @() }
    try {
        $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Matches[1]))
        return @($json | ConvertFrom-Json)
    }
    catch {
        return @()
    }
}

function Invoke-Gh {
    param([string[]] $Arguments)

    if ($DryRun) {
        Write-Host "[dry-run] gh $($Arguments -join ' ')" -ForegroundColor DarkGray
        return ''
    }

    # Keep stdout clean: gh writes progress/warnings to stderr, and mixing them in would
    # corrupt the JSON that callers parse from `gh issue list`.
    $stdout = [System.Collections.Generic.List[string]]::new()
    $stderr = [System.Collections.Generic.List[string]]::new()
    & gh @Arguments 2>&1 | ForEach-Object {
        if ($_ -is [System.Management.Automation.ErrorRecord]) { $stderr.Add([string] $_) }
        else { $stdout.Add([string] $_) }
    }

    if ($LASTEXITCODE -ne 0) {
        throw "gh $($Arguments -join ' ') failed: $((@($stderr) + @($stdout)) -join "`n")"
    }
    return ($stdout -join "`n")
}

# ---------------------------------------------------------------------------

$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
$actions = @($report.Findings | Where-Object { $_.Tier -eq 'Action' })
$currentKeys = @($actions | ForEach-Object { Get-ActionKey $_ })

if (-not $RunUrl -and $env:GITHUB_SERVER_URL -and $env:GITHUB_REPOSITORY -and $env:GITHUB_RUN_ID) {
    $RunUrl = "$env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
}

$body = @(
    $marker
    (ConvertTo-StateComment $currentKeys)
    ''
    'This issue is maintained automatically by the `manifest-drift` workflow. It is refreshed in place on every run and closes itself once the drift is gone.'
    ''
    $report.Markdown
    ''
    $(if ($RunUrl) { "[Latest run]($RunUrl)" } else { '' })
) -join "`n"

if ($BodyOut) {
    $body | Set-Content -LiteralPath $BodyOut -Encoding utf8
    Write-Host "Wrote $BodyOut"
}

# Locate the tracking issue, if any.
$existing = $null
if (-not $DryRun) {
    $listed = Invoke-Gh @('issue', 'list', '--repo', $Repository, '--label', $Label, '--state', 'open',
        '--json', 'number,body,title', '--limit', '50')
    if ($listed) {
        $existing = @($listed | ConvertFrom-Json) | Where-Object { $_.body -and $_.body.Contains($marker) } | Select-Object -First 1
    }
}
else {
    Write-Host '[dry-run] would search for an existing tracking issue' -ForegroundColor DarkGray
}

$bodyFile = Join-Path ([IO.Path]::GetTempPath()) "drift-body-$([guid]::NewGuid()).md"
$body | Set-Content -LiteralPath $bodyFile -Encoding utf8

try {
    if ($actions.Count -eq 0) {
        if ($existing) {
            $comment = "All previously reported manifest drift is resolved as of this run. Closing.`n`n" +
            $(if ($RunUrl) { "[Run]($RunUrl)" } else { '' })
            Invoke-Gh @('issue', 'comment', "$($existing.number)", '--repo', $Repository, '--body', $comment) | Out-Null
            Invoke-Gh @('issue', 'close', "$($existing.number)", '--repo', $Repository, '--reason', 'completed') | Out-Null
            Write-Host "Closed issue #$($existing.number) - no drift remaining."
        }
        else {
            Write-Host 'No drift and no open tracking issue. Nothing to do.'
        }

        if ($env:GITHUB_OUTPUT) { "issue_action=none" | Add-Content -LiteralPath $env:GITHUB_OUTPUT }
        return
    }

    if (-not $existing) {
        # Label may not exist yet; creating it is best-effort.
        try {
            Invoke-Gh @('label', 'create', $Label, '--repo', $Repository, '--color', 'EDB219',
                '--description', 'Pinned SDK/workload versions are behind upstream', '--force') | Out-Null
        }
        catch {
            Write-Warning "Could not ensure label '$Label': $($_.Exception.Message)"
        }

        $arguments = @('issue', 'create', '--repo', $Repository, '--title', $Title, '--label', $Label, '--body-file', $bodyFile)
        foreach ($person in ($Assignee ?? @())) {
            if ($person) { $arguments += @('--assignee', $person) }
        }

        $created = Invoke-Gh $arguments
        Write-Host "Opened tracking issue: $created"

        if ($env:GITHUB_OUTPUT) { "issue_action=created" | Add-Content -LiteralPath $env:GITHUB_OUTPUT }
        return
    }

    $previousKeys = ConvertFrom-StateComment $existing.body
    $added = @($currentKeys | Where-Object { $_ -notin $previousKeys })
    $removed = @($previousKeys | Where-Object { $_ -notin $currentKeys })

    Invoke-Gh @('issue', 'edit', "$($existing.number)", '--repo', $Repository, '--body-file', $bodyFile) | Out-Null

    if ($added.Count -eq 0 -and $removed.Count -eq 0) {
        Write-Host "Issue #$($existing.number) refreshed; drift set unchanged, so no comment was added."
        if ($env:GITHUB_OUTPUT) { "issue_action=refreshed" | Add-Content -LiteralPath $env:GITHUB_OUTPUT }
        return
    }

    $lines = @('The drift set changed since the last run.', '')
    if ($added.Count -gt 0) {
        $lines += '**New:**'
        $lines += ($added | ForEach-Object { "- ``$_``" })
        $lines += ''
    }
    if ($removed.Count -gt 0) {
        $lines += '**No longer reported:**'
        $lines += ($removed | ForEach-Object { "- ``$_``" })
        $lines += ''
    }
    if ($RunUrl) { $lines += "[Run]($RunUrl)" }

    Invoke-Gh @('issue', 'comment', "$($existing.number)", '--repo', $Repository, '--body', ($lines -join "`n")) | Out-Null
    Write-Host "Issue #$($existing.number) updated: $($added.Count) new, $($removed.Count) resolved."

    if ($env:GITHUB_OUTPUT) { "issue_action=updated" | Add-Content -LiteralPath $env:GITHUB_OUTPUT }
}
finally {
    Remove-Item -LiteralPath $bodyFile -ErrorAction SilentlyContinue
}
