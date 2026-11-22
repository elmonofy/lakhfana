# Lakhfana build script (Windows PowerShell 5.1+). Works from any current directory.
#   .\build.ps1          -> dist\Lakhfana.exe, dist\tests\ConverterTests.exe, dist\tests\UiSnapshot.exe
#   .\build.ps1 -Test    -> also runs ConverterTests.exe
# Uses the in-box C# 5 compiler of .NET Framework 4.x. Exits non-zero on any compile error.
param(
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) {
    Write-Host 'csc.exe (.NET Framework 4.x) not found.' -ForegroundColor Red
    exit 1
}

$refs = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:Microsoft.CSharp.dll'
)
$common = @('/nologo', '/noconfig', '/nostdlib-', '/codepage:65001', '/optimize+', '/platform:anycpu',
            '/langversion:5', '/warn:4', '/filealign:512') + $refs

# Relative paths (from the project root) keep non-ASCII folder names out of csc's command line.
function Get-RelativeSources([string]$folder, [string[]]$exclude) {
    $full = Join-Path $root $folder
    if (-not (Test-Path -LiteralPath $full)) { return ,@() }
    $items = @(Get-ChildItem -LiteralPath $full -Recurse -File -Filter '*.cs' | Sort-Object FullName)
    $result = @()
    foreach ($i in $items) {
        $rel = $i.FullName.Substring($root.TrimEnd('\').Length + 1)
        $skip = $false
        foreach ($e in $exclude) { if ($rel -ieq $e) { $skip = $true } }
        if (-not $skip) { $result += $rel }
    }
    return ,$result
}

function Invoke-Csc([string]$label, [string[]]$arguments) {
    Write-Host ''
    Write-Host ('== ' + $label) -ForegroundColor Cyan
    & $csc @arguments
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Write-Host ('FAILED: ' + $label + ' (csc exit ' + $code + ')') -ForegroundColor Red
        exit 1
    }
    Write-Host ('OK: ' + $label) -ForegroundColor Green
}

Push-Location -LiteralPath $root
try {
    New-Item -ItemType Directory -Force -Path 'dist' | Out-Null
    New-Item -ItemType Directory -Force -Path 'dist\tests' | Out-Null

    $iconArgs = @()
    if (Test-Path -LiteralPath 'assets\app.ico') { $iconArgs = @('/win32icon:assets\app.ico') }
    else { Write-Host 'note: assets\app.ico not found, building without an icon' -ForegroundColor Yellow }

    $manifestArgs = @()
    if (Test-Path -LiteralPath 'app.manifest') { $manifestArgs = @('/win32manifest:app.manifest') }

    # ---- app
    $appSources = Get-RelativeSources 'src' @()
    if ($appSources.Count -eq 0) { Write-Host 'no sources under src' -ForegroundColor Red; exit 1 }
    Invoke-Csc 'Lakhfana.exe' ($common + @('/target:winexe', '/out:dist\Lakhfana.exe') + $iconArgs + $manifestArgs + $appSources)

    # ---- converter tests: core + Settings + Hotkey
    if (Test-Path -LiteralPath 'tests\ConverterTests.cs') {
        $coreSources = Get-RelativeSources 'src\Core' @()
        $testSources = @('tests\ConverterTests.cs') + $coreSources + @('src\App\Settings.cs', 'src\App\Hotkey.cs')
        Invoke-Csc 'ConverterTests.exe' ($common + @('/target:exe', '/out:dist\tests\ConverterTests.exe') + $manifestArgs + $testSources)
    } else {
        Write-Host 'note: tests\ConverterTests.cs not found, skipped' -ForegroundColor Yellow
    }

    # ---- UI snapshot tool: everything except Program.cs
    if (Test-Path -LiteralPath 'tests\UiSnapshot.cs') {
        $uiSources = @('tests\UiSnapshot.cs') + (Get-RelativeSources 'src' @('src\Program.cs'))
        Invoke-Csc 'UiSnapshot.exe' ($common + @('/target:exe', '/out:dist\tests\UiSnapshot.exe') + $iconArgs + $manifestArgs + $uiSources)
    } else {
        Write-Host 'note: tests\UiSnapshot.cs not found, skipped' -ForegroundColor Yellow
    }

    if ($Test -and (Test-Path -LiteralPath 'dist\tests\ConverterTests.exe')) {
        Write-Host ''
        Write-Host '== running ConverterTests' -ForegroundColor Cyan
        & '.\dist\tests\ConverterTests.exe'
        if ($LASTEXITCODE -ne 0) { Write-Host 'TESTS FAILED' -ForegroundColor Red; exit 1 }
    }

    Write-Host ''
    Write-Host ('Built: ' + (Join-Path $root 'dist\Lakhfana.exe')) -ForegroundColor Green
}
finally {
    Pop-Location
}
exit 0
