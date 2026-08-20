# Automated verification for published dist folder + unit tests
param(
    [string]$Version = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content -Path (Join-Path $root "VERSION") -Raw).Trim()
}

$distDir = Join-Path $root "dist\callanalog v$Version"
$exePath = Join-Path $distDir "CallAnalog.Softphone.exe"
$failures = New-Object System.Collections.Generic.List[string]
$checks = 0

function Assert-Check {
    param(
        [string]$Name,
        [bool]$Condition,
        [string]$Detail = ""
    )
    $script:checks++
    if ($Condition) {
        Write-Host "[PASS] $Name"
        return
    }

    $message = if ($Detail) { "$Name - $Detail" } else { $Name }
    Write-Host "[FAIL] $message" -ForegroundColor Red
    $script:failures.Add($message)
}

Write-Host "=== CallAnalog Softphone dist verification v$Version ===" -ForegroundColor Cyan
Write-Host ""

if (-not $SkipTests) {
    Write-Host "--- Unit tests ---" -ForegroundColor Yellow
    dotnet test "CallAnalog.Softphone.Tests\CallAnalog.Softphone.Tests.csproj" --configuration Release --verbosity minimal --nologo
    if ($LASTEXITCODE -ne 0) {
        $failures.Add("dotnet test failed with exit code $LASTEXITCODE")
    }
    Write-Host ""
}

Write-Host "--- Dist folder ---" -ForegroundColor Yellow
Assert-Check "Dist folder exists" (Test-Path $distDir) $distDir
Assert-Check "Main executable exists" (Test-Path $exePath) $exePath

$requiredFiles = @(
    "appsettings.json",
    "build-info.txt",
    "CHANGES.txt",
    "CHANGELOG.md",
    "CallAnalog.Softphone.dll",
    "CallAnalog.Softphone.deps.json",
    "CallAnalog.Softphone.runtimeconfig.json",
    "Assets\logo.png",
    "Assets\favicon.png"
)

foreach ($file in $requiredFiles) {
    Assert-Check "Required file: $file" (Test-Path (Join-Path $distDir $file))
}

if (Test-Path (Join-Path $distDir "build-info.txt")) {
    $buildInfo = Get-Content (Join-Path $distDir "build-info.txt") -Raw
    Assert-Check "build-info Version=$Version" ($buildInfo -match "Version=$Version")
}

if (Test-Path (Join-Path $distDir "appsettings.json")) {
    $appsettings = Get-Content (Join-Path $distDir "appsettings.json") -Raw
    Assert-Check "appsettings version" ($appsettings -match "`"Version`"\s*:\s*`"$Version`"")
}

if (Test-Path (Join-Path $root "VERSION")) {
    $repoVersion = (Get-Content (Join-Path $root "VERSION") -Raw).Trim()
    Assert-Check "Repo VERSION matches target" ($repoVersion -eq $Version) "repo=$repoVersion target=$Version"
}

if (Test-Path $exePath) {
    $verInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
    Assert-Check "Exe FileVersion starts with $Version" ($verInfo.FileVersion -like "$Version*") "FileVersion=$($verInfo.FileVersion)"
    Assert-Check "Exe ProductVersion contains $Version" ($verInfo.ProductVersion -like "*$Version*") "ProductVersion=$($verInfo.ProductVersion)"
}

Write-Host ""
Write-Host "--- Repo metadata ---" -ForegroundColor Yellow
$changelog = Get-Content (Join-Path $root "CHANGELOG.md") -Raw
Assert-Check "CHANGELOG has section ## $Version" ($changelog -match "(?m)^## $Version\s*$")

$csproj = Get-Content (Join-Path $root "CallAnalog.Softphone.csproj") -Raw
Assert-Check "csproj Version tag" ($csproj -match "<Version>$Version</Version>")

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "Checks run: $checks"
Write-Host "Failures: $($failures.Count)"

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Failed checks:" -ForegroundColor Red
    foreach ($f in $failures) {
        Write-Host "  - $f" -ForegroundColor Red
    }
    exit 1
}

Write-Host "All automated checks passed." -ForegroundColor Green
exit 0
