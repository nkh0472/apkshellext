# Confirms a Release build produced the complete publish layout. Kept in the repo
# (instead of inline in ci.yml) so the same checks can be run locally before pushing.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dotnet', 'vs')]
    [string]$Route,

    [string]$OutputDir = 'ApkShellext2\bin\Release'
)

$ErrorActionPreference = 'Stop'

# Version-to-package-name rule lives only in eng\package-name.ps1; both gates
# and Resources\postbuild.cmd call into it so they cannot drift apart.
. (Join-Path $PSScriptRoot 'package-name.ps1')
$packageName = Get-ApkShellextPackageName -VersionFile (Join-Path $PSScriptRoot '..\version.txt')

# Same 8 files the postbuild 7z manifest packs. A missing one means the publish
# folder is incomplete; the packaging step would fail too, but its error is easy
# to lose among the compiler output.
$manifest = @(
    'ApkShellext2.dll', 'install.bat', 'install_user.bat', 'uninstall.bat',
    'uninstall_user.bat', 'restart_explorer.bat', 'libwebp_x86.dll', 'libwebp_x64.dll'
)
$langs = @(
    'ar-SA', 'de-DE', 'el-GR', 'es-ES', 'fa-IR', 'fr-FR', 'he-IL', 'it-IT', 'ja-JP',
    'ko-KR', 'my-MM', 'nl-NL', 'pl-PL', 'pt-PT', 'ru-RU', 'tr-TR', 'uk-UA', 'zh-CN', 'zh-TW'
)

$missing = @($manifest | Where-Object { -not (Test-Path (Join-Path $OutputDir $_)) })
if ($missing.Count -gt 0) {
    throw "missing deployment files: $($missing -join ', ')"
}

$missingSatellites = @($langs | Where-Object { -not (Test-Path (Join-Path $OutputDir "$_\ApkShellext2.resources.dll")) })
if ($missingSatellites.Count -gt 0) {
    throw "missing satellite resources: $($missingSatellites -join ', ')"
}

if ($Route -eq 'vs') {
    # Preserialized resources are a dotnet-CLI-only compile artifact; a VS-route
    # output carrying this dll would be a packaging regression (see
    # compile_guidance.md §8 and the exclusions in Resources\postbuild.cmd).
    if (Test-Path (Join-Path $OutputDir 'System.Resources.Extensions.dll')) {
        throw 'System.Resources.Extensions.dll must not exist in a VS-route output'
    }
}
else {
    Write-Host 'dotnet route: System.Resources.Extensions.dll is expected here (preserialized resources) and is not part of the publish manifest.'
}

"layout OK ($Route): $($manifest.Count) root files + $($langs.Count) language directories"

$sevenZPath = Join-Path $OutputDir $packageName
if (-not (Test-Path $sevenZPath)) {
    throw "$packageName is missing - the 7-Zip packaging step did not run"
}

$dll = Get-Item (Join-Path $OutputDir 'ApkShellext2.dll')
$sevenZ = Get-Item $sevenZPath
"dll bytes: $($dll.Length)"
"7z bytes:  $($sevenZ.Length)"
