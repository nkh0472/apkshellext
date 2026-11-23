# Size report for the publish artifacts.
#
# A package that swallows a dependency shows up here long before anyone notices it in
# Explorer, but size is guidance, not a build contract: this script always exits 0 and
# only prints how the artifacts compare with the reference budgets. Raising or lowering a
# budget is a documentation change, not a red build. The functional contract next door
# (`check-layout.ps1`) is what actually fails CI.
#
# Reference point (VS route, clean Release): dll 1,181,184 B, 7z ~752,500 B (archive
# timestamps vary), tree 2,360,097 B / 27 files. The budgets in the param block sit
# above that point and are only used to label a line OVER, never to fail.
param(
    [string]$OutputDir = 'ApkShellext2\bin\Release',

    # Reference budgets, overridable so a report can be rendered against a tighter number
    # without editing the script.
    [long]$MaxDllBytes = 1260000,
    [long]$MaxPackageBytes = 1000000,
    [long]$MaxTreeBytes = 2500000
)

$ErrorActionPreference = 'Stop'

# Version-to-package-name rule lives only in eng\package-name.ps1; both scripts
# and Resources\postbuild.cmd call into it so they cannot drift apart.
. (Join-Path $PSScriptRoot 'package-name.ps1')
$packageName = Get-ApkShellextPackageName -VersionFile (Join-Path $PSScriptRoot '..\version.txt')

# The tree is counted from the same 9 manifest files the postbuild 7z packs plus
# the 19 satellite resource assemblies, so the number does not move when the
# developer leaves extra files (pdb, logs, stale stdout) in bin\Release.
$manifest = @(
    'ApkShellext2.dll', 'install.bat', 'install_user.bat', 'uninstall.bat',
    'uninstall_user.bat', 'restart_explorer.bat', 'update.bat', 'libwebp_x86.dll', 'libwebp_x64.dll'
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

# A missing file is still an error: that means the build or the layout check never ran,
# and a size report over a half-built tree would be misleading.
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

$rows = @(
    @{ Name = 'ApkShellext2.dll'; Bytes = $dllBytes;     Max = $MaxDllBytes },
    @{ Name = $packageName;    Bytes = $packageBytes; Max = $MaxPackageBytes },
    @{ Name = "deployment tree ($($treeFiles.Count) files)"; Bytes = $treeBytes; Max = $MaxTreeBytes }
)

$over = 0
foreach ($row in $rows) {
    $isOver = $row.Bytes -gt $row.Max
    if ($isOver) { $over++ }
    '{0,-38} {1,10} B  reference {2,10} B  ({3}%)  {4}' -f $row.Name, $row.Bytes, $row.Max,
        [math]::Round(100.0 * $row.Bytes / $row.Max, 1),
        $(if ($isOver) { 'OVER (guidance only)' } else { 'ok' })
}

if ($over -gt 0) {
    Write-Host "size report: $over artifact(s) above the reference budget - guidance only, CI stays green"
} else {
    Write-Host 'size report: all artifacts within the reference budget'
}

# Always 0: size never gates a merge.
exit 0
