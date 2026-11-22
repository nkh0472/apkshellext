# Single source for the release package file name: ApkShellext2-<ver>.7z.
# The version comes from version.txt (the same file the assembly version is read
# from), and a trailing ".0" revision segment is dropped so the asset name
# matches the release assets: 3.5.1.0 -> ApkShellext2-3.5.1.7z, while 9.9.9.9
# keeps all four segments. Both the packaging step (Resources\postbuild.cmd runs
# this file with -File) and the two gates (eng\check-layout.ps1 and
# eng\check-size.ps1 dot-source it) go through Get-ApkShellextPackageName, so the
# rule is implemented exactly once.
param(
    [string]$VersionFile
)

function Get-ApkShellextPackageName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VersionFile
    )

    if (-not (Test-Path $VersionFile)) {
        throw "version file not found: $VersionFile"
    }
    $version = ([System.IO.File]::ReadAllText($VersionFile)).Trim()
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "version file is empty: $VersionFile"
    }
    # Strip only the trailing revision segment; keep 4-part versions otherwise.
    $version = $version -replace '\.0$', ''
    "ApkShellext2-$version.7z"
}

# Dot-sourced by the gates: only define the function. Executed as a script
# (-File, the packaging step): print the name on stdout for the caller.
if ($MyInvocation.InvocationName -ne '.') {
    Get-ApkShellextPackageName -VersionFile $VersionFile
}
