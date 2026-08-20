param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$BumpMinor,
    [switch]$BumpMajor
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Get-GitValue {
    param([string]$Command)
    try {
        $value = Invoke-Expression $Command 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
            return $null
        }
        return $value.Trim()
    }
    catch {
        return $null
    }
}

function Read-AppVersion {
    param([string]$Path)
    $raw = (Get-Content -Path $Path -Raw).Trim()
    if ($raw -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
        throw "VERSION must be major.minor.patch (e.g. 2.0.0). Found: '$raw'"
    }
    return [pscustomobject]@{
        Major = [int]$Matches[1]
        Minor = [int]$Matches[2]
        Patch = [int]$Matches[3]
        Text = $raw
    }
}

function Write-AppVersion {
    param([string]$Path, [string]$Version)
    Set-Content -Path $Path -Value $Version -Encoding UTF8 -NoNewline
}

function Bump-AppVersion {
    param(
        [pscustomobject]$Version,
        [bool]$Minor,
        [bool]$Major
    )

    if ($Major) {
        $nextMajor = $Version.Major + 1
        return [pscustomobject]@{
            Major = $nextMajor
            Minor = 0
            Patch = 0
            Text = "$nextMajor.0.0"
        }
    }

    if ($Minor) {
        return [pscustomobject]@{
            Major = $Version.Major
            Minor = $Version.Minor
            Patch = $Version.Patch + 1
            Text = "$($Version.Major).$($Version.Minor).$($Version.Patch + 1)"
        }
    }

    return $Version
}

function Get-VersionedOutputDir {
    param([string]$VersionText)
    Join-Path $root ("dist\callanalog v{0}" -f $VersionText)
}

function Set-AppSettingsVersion {
    param([string]$Path, [string]$VersionText)
    $content = Get-Content -Path $Path -Raw
    $updated = $content -replace '("Version"\s*:\s*")[^"]+(")', "`${1}$VersionText`${2}"
    if ($updated -ne $content) {
        Set-Content -Path $Path -Value $updated -Encoding UTF8 -NoNewline
    }
}

function Get-ChangelogSection {
    param(
        [string]$ChangelogPath,
        [string]$VersionText
    )

    if (-not (Test-Path $ChangelogPath)) {
        throw "Missing CHANGELOG.md. Add a '## $VersionText' section before building."
    }

    $lines = Get-Content -Path $ChangelogPath
    $header = "## $VersionText"
    $start = [array]::IndexOf($lines, $header)
    if ($start -lt 0) {
        throw "CHANGELOG.md has no section '$header'. Document changes before building version $VersionText."
    }

    $body = New-Object System.Collections.Generic.List[string]
    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^##\s+\d+\.\d+\.\d+\s*$') {
            break
        }
        $body.Add($line)
    }

    $text = ($body -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($text) -or $text -match 'TODO:\s*document changes') {
        throw "CHANGELOG.md section '$header' is empty or still a TODO. Add release notes before building."
    }

    return $text
}

function Write-VersionChangesFile {
    param(
        [string]$OutputDir,
        [string]$VersionText,
        [string]$Body,
        [string]$BuiltUtc,
        [string]$GitCommit
    )

    $commitLine = if ($GitCommit) { "Git commit: $GitCommit" } else { "Git commit: (not available)" }
    $content = @(
        "CallAnalog Softphone v$VersionText"
        "Built (UTC): $BuiltUtc"
        $commitLine
        ""
        "CHANGES IN THIS VERSION"
        "======================="
        ""
        $Body
        ""
    ) -join [Environment]::NewLine

    $changesPath = Join-Path $OutputDir "CHANGES.txt"
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($changesPath, $content, $utf8Bom)
}

function Ensure-ChangelogStub {
    param(
        [string]$ChangelogPath,
        [string]$VersionText
    )

    if (-not (Test-Path $ChangelogPath)) {
        @(
            "# Changelog"
            ""
            "## $VersionText"
            ""
            "- TODO: document changes for this release"
            ""
        ) | Set-Content -Path $ChangelogPath -Encoding UTF8
        return
    }

    $raw = Get-Content -Path $ChangelogPath -Raw
    if ($raw -match "(?m)^##\s+$([regex]::Escape($VersionText))\s*$") {
        return
    }

    $stub = @(
        ""
        "## $VersionText"
        ""
        "- TODO: document changes for this release"
        ""
    ) -join [Environment]::NewLine

    Add-Content -Path $ChangelogPath -Value $stub -Encoding UTF8
}

$versionFile = Join-Path $root "VERSION"
$appSettingsPath = Join-Path $root "appsettings.json"
$version = Read-AppVersion -Path $versionFile

if ($BumpMinor -and $BumpMajor) {
    throw "Use only one of -BumpMinor or -BumpMajor."
}

$version = Bump-AppVersion -Version $version -Minor:$BumpMinor.IsPresent -Major:$BumpMajor.IsPresent
Write-AppVersion -Path $versionFile -Version $version.Text
Set-AppSettingsVersion -Path $appSettingsPath -VersionText $version.Text

$changelogPath = Join-Path $root "CHANGELOG.md"
if ($BumpMinor.IsPresent -or $BumpMajor.IsPresent) {
    Ensure-ChangelogStub -ChangelogPath $changelogPath -VersionText $version.Text
    Write-Host "Reminder: edit CHANGELOG.md section '## $($version.Text)' before publishing."
}

$changelogBody = Get-ChangelogSection -ChangelogPath $changelogPath -VersionText $version.Text

$outputDir = Get-VersionedOutputDir -VersionText $version.Text
$gitCommit = Get-GitValue "git rev-parse --short HEAD"
$gitBranch = Get-GitValue "git rev-parse --abbrev-ref HEAD"
$buildUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$informationalVersion = if ($gitCommit) { "$($version.Text)+$gitCommit" } else { "$($version.Text)+local" }
$fileVersion = "{0}.0" -f $version.Text

Write-Host "Building CallAnalog Softphone $($version.Text)"
Write-Host "Output: $outputDir"

dotnet restore
dotnet build -c $Configuration `
    /p:Version=$($version.Text) `
    /p:FileVersion=$fileVersion `
    /p:InformationalVersion=$informationalVersion

$buildInfoPath = Join-Path $root "build-info.txt"
@(
    "Version=$($version.Text)"
    "Commit=$gitCommit"
    "Branch=$gitBranch"
    "BuiltUtc=$buildUtc"
    "OutputDir=$outputDir"
) | Set-Content -Path $buildInfoPath -Encoding UTF8

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
dotnet publish -c $Configuration -r $Runtime --self-contained true -o $outputDir `
    /p:PublishReadyToRun=true `
    /p:Version=$($version.Text) `
    /p:FileVersion=$fileVersion `
    /p:InformationalVersion=$informationalVersion
Copy-Item -Path $buildInfoPath -Destination (Join-Path $outputDir "build-info.txt") -Force
Write-VersionChangesFile -OutputDir $outputDir -VersionText $version.Text -Body $changelogBody -BuiltUtc $buildUtc -GitCommit $gitCommit
Copy-Item -Path $changelogPath -Destination (Join-Path $outputDir "CHANGELOG.md") -Force
Copy-Item -Path (Join-Path $root "docs\MANUAL_TESTING_SOP.md") -Destination (Join-Path $outputDir "MANUAL_TESTING_SOP.md") -Force

Write-Host "Publish complete: $(Resolve-Path $outputDir)"
Write-Host ""
Write-Host "Next builds:"
Write-Host "  Minor change: .\build.ps1 -BumpMinor"
Write-Host "  Major change: .\build.ps1 -BumpMajor"
Write-Host "  Rebuild same version (overwrite folder): .\build.ps1"
