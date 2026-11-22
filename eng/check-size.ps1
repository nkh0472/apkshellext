# Size gate for the publish artifacts. The repository bundles native and managed
# dependencies (LibWebP, System.Memory, ...), so a stray reference shows
# up as package growth long before anyone notices it in Explorer - a hard byte limit
# is the only cheap way to catch that in CI.
#
# Baseline measured on v3.5.1 (VS route, clean Release, 7-Zip 26.06):
#   ApkShellext2.dll            1,573,888 B
#   ApkShellext2-3.5.1.7z        1,028,101 B
#   deployment tree             3,392,793 B over 32 files (13 manifest + 19 satellites)
# T-H dropped the ExCSS dependency (2026-11-23, VS route): dll 1,551,360 B,
# 7z ~926,000 B (archive timestamps vary by a few hundred bytes), tree 3,027,225 B
# over 31 files (12 manifest + 19 satellites).
# T-I trimmed the unused SVG vendor subtrees (2026-11-23, VS route): dll 1,356,288 B,
# 7z ~886,900 B (archive timestamps vary by a few hundred bytes), tree 2,832,153 B
# over 31 files (12 manifest + 19 satellites).
# T-J replaced the source-embedded SharpZipLib with System.IO.Compression and dropped
# the submodule (2026-11-23, VS route): dll 1,165,824 B, 7z ~826,400 B (archive
# timestamps vary by a few hundred bytes), tree 2,641,689 B over 31 files (12 manifest
# + 19 satellites). That is 192,000 B below T-I: the zip container reader now comes from
# the framework, and the only replacement code is the capped reader layer in ZipSafety.cs
# plus a small zlib/CRC-32 shim for the pinned PNGDecrush source.
# The limits below keep ~5% headroom over that: enough for routine edits, tight
# enough that a newly bundled dependency trips the gate. Phase 2 targets a <=2.5 MB
# package, so these limits are meant to be tightened then, never relaxed silently.
param(
    [string]$OutputDir = 'ApkShellext2\bin\Release',

    # Overridable for the negative test (copy of the defaults, only smaller values
    # ever make sense); the CI job uses the defaults.
    [long]$MaxDllBytes = 1650000,
    [long]$MaxPackageBytes = 1100000,
    [long]$MaxTreeBytes = 3600000
)

$ErrorActionPreference = 'Stop'

# Version-to-package-name rule lives only in eng\package-name.ps1; both gates
# and Resources\postbuild.cmd call into it so they cannot drift apart.
. (Join-Path $PSScriptRoot 'package-name.ps1')
$packageName = Get-ApkShellextPackageName -VersionFile (Join-Path $PSScriptRoot '..\version.txt')

# The tree is counted from the same 12 manifest files the postbuild 7z packs plus
# the 19 satellite resource assemblies, so the number does not move when the
# developer leaves extra files (pdb, logs, stale stdout) in bin\Release.
$manifest = @(
    'ApkShellext2.dll', 'install.bat', 'install_user.bat', 'uninstall.bat',
    'uninstall_user.bat', 'restart_explorer.bat', 'libwebp_x86.dll', 'libwebp_x64.dll',
    'System.Memory.dll', 'System.Buffers.dll',
    'System.Runtime.CompilerServices.Unsafe.dll', 'System.Numerics.Vectors.dll'
)
$langs = @(
    'ar-SA', 'de-DE', 'el-GR', 'es-ES', 'fa-IR', 'fr-FR', 'he-IL', 'it-IT', 'ja-JP',
    'ko-KR', 'my-MM', 'nl-NL', 'pl-PL', 'pt-PT', 'ru-RU', 'tr-TR', 'uk-UA', 'zh-CN', 'zh-TW'
)

$treeFiles = @(
    $manifest | ForEach-Object { Join-Path $OutputDir $_ }
) + @(
    $langs | ForEach-Object { Join-Path $OutputDir "$_\ApkShellext2.resources.dll" }
)

foreach ($path in $treeFiles) {
    if (-not (Test-Path $path)) {
        throw "missing deployment file: $path (run check-layout.ps1 first)"
    }
}

$packagePath = Join-Path $OutputDir $packageName
if (-not (Test-Path $packagePath)) {
    throw "missing package: $packagePath"
}

$dllBytes = (Get-Item (Join-Path $OutputDir 'ApkShellext2.dll')).Length
$packageBytes = (Get-Item $packagePath).Length
$treeBytes = ($treeFiles | ForEach-Object { (Get-Item $_).Length } | Measure-Object -Sum).Sum

$failed = $false
$rows = @(
    @{ Name = 'ApkShellext2.dll'; Bytes = $dllBytes;     Max = $MaxDllBytes },
    @{ Name = $packageName;    Bytes = $packageBytes; Max = $MaxPackageBytes },
    @{ Name = "deployment tree ($($treeFiles.Count) files)"; Bytes = $treeBytes; Max = $MaxTreeBytes }
)

foreach ($row in $rows) {
    $over = $row.Bytes -gt $row.Max
    if ($over) { $failed = $true }
    '{0,-38} {1,10} B  limit {2,10} B  ({3}%)  {4}' -f $row.Name, $row.Bytes, $row.Max,
        [math]::Round(100.0 * $row.Bytes / $row.Max, 1),
        $(if ($over) { 'OVER' } else { 'ok' })
}

if ($failed) {
    Write-Host 'size gate FAILED: a package grew past its budget'
    exit 1
}

Write-Host 'size gate OK'
exit 0
