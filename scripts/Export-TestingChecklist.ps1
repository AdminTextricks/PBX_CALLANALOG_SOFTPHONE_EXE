# Generates Excel checklist from docs/MANUAL_TESTING_SOP.md
param(
    [string]$SourceMd = "",
    [string]$OutputXlsx = "",
    [string]$OutputCsv = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$version = (Get-Content -Path (Join-Path $root "VERSION") -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($SourceMd)) {
    $SourceMd = Join-Path $root "docs\MANUAL_TESTING_SOP.md"
}
if ([string]::IsNullOrWhiteSpace($OutputXlsx)) {
    $OutputXlsx = Join-Path $root "docs\CallAnalog-Softphone-v$version-Testing-Checklist.xlsx"
}
if ([string]::IsNullOrWhiteSpace($OutputCsv)) {
    $OutputCsv = Join-Path $root "docs\CallAnalog-Softphone-v$version-Testing-Checklist.csv"
}

function Clean-Cell([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return "" }
    return ($text -replace '\*\*', '' -replace '`', '').Trim()
}

$lines = Get-Content -Path $SourceMd -Encoding UTF8
$currentSection = "General"
$rows = New-Object System.Collections.Generic.List[object]

foreach ($line in $lines) {
    if ($line -match '^## (.+)$') {
        $currentSection = $Matches[1].Trim()
        continue
    }
    if ($line -match '^### (.+)$') {
        $currentSection = "$currentSection / $($Matches[1].Trim())"
        continue
    }
    if (-not ($line -match '^\|')) { continue }
    if ($line -match '^\|\s*----') { continue }

    $parts = $line.Split('|') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
    if ($parts.Count -lt 3) { continue }

    $col0 = $parts[0]

    # Standard: ID | Steps | Expected | P | F | Notes
    if ($col0 -match '^[A-Z0-9]+-[0-9]+$') {
        $note = if ($parts.Count -ge 6) { Clean-Cell $parts[5] } else { "" }
        $rows.Add([pscustomobject]@{
            Section        = $currentSection
            TestID         = $col0
            Steps          = (Clean-Cell $parts[1])
            ExpectedResult = (Clean-Cell $parts[2])
            Pass           = ""
            Fail           = ""
            Blocked        = ""
            Notes          = $note
            Tester         = ""
            TestDate       = ""
        })
        continue
    }

    # Smoke: # | Test | ID ref (section 24 only)
    if ($currentSection -match 'Regression smoke' -and $col0 -match '^\d+$') {
        $rows.Add([pscustomobject]@{
            Section        = $currentSection
            TestID         = "SMK-$($col0.PadLeft(2,'0'))"
            Steps          = (Clean-Cell $parts[1])
            ExpectedResult = "Must pass. Ref: $(Clean-Cell $parts[2])"
            Pass           = ""
            Fail           = ""
            Blocked        = ""
            Notes          = ""
            Tester         = ""
            TestDate       = ""
        })
    }
}

$meta = @(
    [pscustomobject]@{ Field = "Product"; Value = "CallAnalog Softphone" },
    [pscustomobject]@{ Field = "Build version"; Value = $version },
    [pscustomobject]@{ Field = "Document"; Value = "MANUAL_TESTING_SOP.md" },
    [pscustomobject]@{ Field = "Generated"; Value = (Get-Date -Format "yyyy-MM-dd HH:mm") },
    [pscustomobject]@{ Field = "Total test cases"; Value = $rows.Count },
    [pscustomobject]@{ Field = "Result codes"; Value = "P=Pass, F=Fail, B=Blocked" },
    [pscustomobject]@{ Field = "Primary extension"; Value = "" },
    [pscustomobject]@{ Field = "Second extension"; Value = "" },
    [pscustomobject]@{ Field = "External number"; Value = "" },
    [pscustomobject]@{ Field = "Tester"; Value = "" },
    [pscustomobject]@{ Field = "Test date"; Value = "" },
    [pscustomobject]@{ Field = "Release recommendation"; Value = "" }
)

$rows | Export-Csv -Path $OutputCsv -NoTypeInformation -Encoding UTF8
Write-Host "CSV: $OutputCsv ($($rows.Count) test cases)"

$excelCreated = $false
try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $wb = $excel.Workbooks.Add()

    while ($wb.Worksheets.Count -gt 1) {
        $wb.Worksheets.Item($wb.Worksheets.Count).Delete()
    }

    $ws = $wb.Worksheets.Item(1)
    $ws.Name = "Checklist"

    $headers = @("Section", "Test ID", "Steps", "Expected Result", "Pass (P)", "Fail (F)", "Blocked (B)", "Notes", "Tester", "Date")
    for ($c = 0; $c -lt $headers.Count; $c++) {
        $cell = $ws.Cells.Item(1, $c + 1)
        $cell.Value2 = $headers[$c]
        $cell.Font.Bold = $true
        $cell.Interior.Color = 0xFFE8F0FE
    }

    $r = 2
    foreach ($item in $rows) {
        $ws.Cells.Item($r, 1).Value2 = $item.Section
        $ws.Cells.Item($r, 2).Value2 = $item.TestID
        $ws.Cells.Item($r, 3).Value2 = $item.Steps
        $ws.Cells.Item($r, 4).Value2 = $item.ExpectedResult
        $ws.Cells.Item($r, 5).Value2 = $item.Pass
        $ws.Cells.Item($r, 6).Value2 = $item.Fail
        $ws.Cells.Item($r, 7).Value2 = $item.Blocked
        $ws.Cells.Item($r, 8).Value2 = $item.Notes
        $ws.Cells.Item($r, 9).Value2 = $item.Tester
        $ws.Cells.Item($r, 10).Value2 = $item.TestDate
        $r++
    }

    $lastRow = [Math]::Max(2, $r - 1)
    $ws.Columns.Item("A").ColumnWidth = 28
    $ws.Columns.Item("B").ColumnWidth = 10
    $ws.Columns.Item("C").ColumnWidth = 48
    $ws.Columns.Item("D").ColumnWidth = 48
    $ws.Columns.Item("E").ColumnWidth = 8
    $ws.Columns.Item("F").ColumnWidth = 8
    $ws.Columns.Item("G").ColumnWidth = 10
    $ws.Columns.Item("H").ColumnWidth = 24
    $ws.Columns.Item("I").ColumnWidth = 14
    $ws.Columns.Item("J").ColumnWidth = 12
    $ws.Rows.WrapText = $true
    $ws.Application.ActiveWindow.SplitRow = 1
    $ws.Application.ActiveWindow.FreezePanes = $true
    if ($lastRow -gt 1) {
        $ws.Range("A1:J1").AutoFilter() | Out-Null
        $validationRange = $ws.Range("E2:G$lastRow")
        $validation = $validationRange.Validation
        $validation.Delete()
        $validation.Add(3, 1, 1, "P,F,B") | Out-Null
        $validation.IgnoreBlank = $true
        $validation.InCellDropdown = $true
    }

    $ws2 = $wb.Worksheets.Add([Type]::Missing, $ws)
    $ws2.Name = "Instructions"
    $mr = 1
    foreach ($m in $meta) {
        $ws2.Cells.Item($mr, 1).Value2 = $m.Field
        $ws2.Cells.Item($mr, 1).Font.Bold = $true
        $ws2.Cells.Item($mr, 2).Value2 = [string]$m.Value
        $mr++
    }
    $mr += 1
    @(
        "How to use this checklist",
        "1. Fill in test account info above.",
        "2. Run smoke tests first (SMK-xx rows).",
        "3. Use P / F / B dropdown in columns E-G.",
        "4. Add notes on failure; attach sip.log",
        "5. Filter by Section to test one area at a time.",
        "6. Release gate: SMK-01 through SMK-11 must pass.",
        "",
        "Log: %LOCALAPPDATA%\CallAnalog\logs\sip.log",
        "Installer: installer\output\CallAnalogSoftphone-Setup-$version.exe"
    ) | ForEach-Object {
        $ws2.Cells.Item($mr, 1).Value2 = $_
        $mr++
    }
    $ws2.Columns.Item("A").ColumnWidth = 22
    $ws2.Columns.Item("B").ColumnWidth = 60

    $ws3 = $wb.Worksheets.Add([Type]::Missing, $ws2)
    $ws3.Name = "Summary"
    $ws3.Cells.Item(1, 1).Value2 = "Section"
    $ws3.Cells.Item(1, 2).Value2 = "Total"
    $ws3.Cells.Item(1, 3).Value2 = "Pass"
    $ws3.Cells.Item(1, 4).Value2 = "Fail"
    $ws3.Cells.Item(1, 5).Value2 = "Blocked"
    $ws3.Cells.Item(1, 6).Value2 = "Not run"
    $ws3.Range("A1:F1").Font.Bold = $true

    $sr = 2
    foreach ($g in ($rows | Group-Object Section | Sort-Object Name)) {
        $ws3.Cells.Item($sr, 1).Value2 = $g.Name
        $ws3.Cells.Item($sr, 2).Value2 = [string]$g.Count
        $sr++
    }

    if (Test-Path $OutputXlsx) { Remove-Item $OutputXlsx -Force }
    $wb.SaveAs($OutputXlsx)
    $wb.Close($false)
    $excel.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($wb) | Out-Null
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    $excelCreated = $true
    Write-Host "XLSX: $OutputXlsx ($($rows.Count) test cases)"
}
catch {
    Write-Warning "Excel COM unavailable: $($_.Exception.Message)"
    Write-Host "Open the CSV in Excel: $OutputCsv"
}

$distDir = Join-Path $root "dist\callanalog v$version"
if (Test-Path $distDir) {
    Copy-Item $OutputCsv (Join-Path $distDir "CallAnalog-Softphone-v$version-Testing-Checklist.csv") -Force
    Copy-Item (Join-Path $root "docs\MANUAL_TESTING_SOP.md") (Join-Path $distDir "MANUAL_TESTING_SOP.md") -Force
    if ($excelCreated) {
        Copy-Item $OutputXlsx (Join-Path $distDir "CallAnalog-Softphone-v$version-Testing-Checklist.xlsx") -Force
    }
}
