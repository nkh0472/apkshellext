# Checks a release tag against the checkout, versions and the built package.
# The release workflow calls this script, and a developer can call it by hand
# with the same arguments, so the publish rules can be exercised without a push.
# The tag name is the only input: everything else is derived from version.txt,
# the git tags and the Release output, which keeps the workflow free of logic.
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [string]$BuildDir = 'ApkShellext2\bin\Release',

    [string]$TestResultsDir = 'tests\ApkShellext2.Tests\TestResults'
)

$ErrorActionPreference = 'Stop'

# "v" + 3 or 4 numeric parts, optionally followed by a prerelease suffix.
# The suffix is allowed because the release workflow can publish a prerelease.
$tagPattern = '^v(?<numeric>\d+(\.\d+){2,3})(?<prerelease>-[0-9A-Za-z.]+)?$'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Stop-Check([string]$Message) {
    Write-Host "release check FAILED: $Message"
    if ($env:GITHUB_STEP_SUMMARY) {
        "release check FAILED: $Message" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append
    }
    exit 1
}

function Get-TagVersion([string]$Name) {
    if ($Name -notmatch $tagPattern) {
        return $null
    }
    return [version]$Matches['numeric']
}

# 1. The tag must be a version tag at all; "3.5.1" and "release-1" are rejected
# before any git lookup so the error names the real problem.
if ($Tag -notmatch $tagPattern) {
    Stop-Check "tag '$Tag' does not match 'v<major>.<minor>.<patch>[.<revision>][-prerelease]'"
}

$tagVersion = [version]$Matches['numeric']

# 2. Releases must move forward. git sorts the tags for us, and the newest
# published version is the highest tag other than the one being released.
# This runs before the version.txt comparison so a re-run of an already
# published tag reports "not greater" instead of a confusing version mismatch.
$publishedTags = @(git tag --sort=-v:refname) | Where-Object { $_ -match $tagPattern -and $_ -ne $Tag }
$previousTag = $publishedTags | Select-Object -First 1
if ($previousTag) {
    $previousVersion = Get-TagVersion $previousTag
    if ($previousVersion -and $tagVersion -le $previousVersion) {
        Stop-Check "version $tagVersion is not greater than the latest published tag $previousTag ($previousVersion)"
    }
}

# 3. The numeric part must equal "v" + version.txt without its trailing ".0",
# so a tag can never publish an assembly whose version disagrees with the tag.
# Only the numeric part is compared: a prerelease tag such as v4.0.0-beta.1 has
# to pass against version.txt 4.0.0.0, otherwise the documented prerelease flow
# could never be used.
. (Join-Path $PSScriptRoot 'package-name.ps1')
$versionFile = Join-Path $repoRoot 'version.txt'
$packageName = Get-ApkShellextPackageName -VersionFile $versionFile
$version = $packageName -replace '^ApkShellext2-', '' -replace '\.7z$', ''
$expectedTag = "v$version"
if ("v$($tagVersion)" -ne $expectedTag) {
    Stop-Check "tag $Tag does not match version.txt $((Get-Content $versionFile -Raw).Trim()) (expected $expectedTag)"
}

# 4. Only annotated tags carry a tagger and a message; lightweight tags are
# rejected so the release page always has something to point at.
$objectType = $null
if (git rev-parse -q --verify "refs/tags/$Tag" 2>$null) {
    $objectType = (git cat-file -t "refs/tags/$Tag").Trim()
}
if (-not $objectType) {
    Stop-Check "tag $Tag does not exist; create it with 'git tag -a $Tag -m <message>'"
}
if ($objectType -ne 'tag') {
    Stop-Check "tag $Tag is a lightweight tag; releases require an annotated tag"
}

# 5. A release must come from the main line, otherwise a stray branch commit
# would be published as a release.
$tagCommit = (git rev-parse "$Tag^{commit}").Trim()
if (-not (git rev-parse -q --verify 'origin/main')) {
    Stop-Check 'origin/main is missing; run git fetch origin main first'
}
git merge-base --is-ancestor $tagCommit origin/main
if ($LASTEXITCODE -ne 0) {
    Stop-Check "tag $Tag ($tagCommit) is not an ancestor of origin/main"
}

# 6. The package and the publish tree must match what the gates approved.
$dllPath = Join-Path $BuildDir 'ApkShellext2.dll'
$packagePath = Join-Path $BuildDir $packageName
if (-not (Test-Path $dllPath)) {
    Stop-Check "ApkShellext2.dll not found in $BuildDir; build the Release configuration first"
}
if (-not (Test-Path $packagePath)) {
    Stop-Check "package $packageName not found in $BuildDir; build the Release configuration first"
}

$languageDirs = @(Get-ChildItem -Path $BuildDir -Directory | Where-Object {
    Test-Path (Join-Path $_.FullName 'ApkShellext2.resources.dll')
})
if ($languageDirs.Count -ne 19) {
    Stop-Check "expected 19 language directories, found $($languageDirs.Count)"
}

$dllBytes = (Get-Item $dllPath).Length
$packageBytes = (Get-Item $packagePath).Length
$dllSha256 = (Get-FileHash -Path $dllPath -Algorithm SHA256).Hash
$packageSha256 = (Get-FileHash -Path $packagePath -Algorithm SHA256).Hash

# 7. The test count is reported on the release summary when the workflow left a
# trx file behind; a missing file is not a failure, the count is then unknown.
$testCount = 0
if (Test-Path $TestResultsDir) {
    $trx = Get-ChildItem -Path $TestResultsDir -Filter '*.trx' -Recurse |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($trx) {
        $testCount = [regex]::Matches((Get-Content $trx.FullName -Raw), '<UnitTestResult\b').Count
    }
}

$prerelease = if ($Tag -match '-') { 'true' } else { 'false' }

$lines = @(
    "release check OK: $Tag"
    "version: $version (from version.txt)"
    "previous tag: $previousTag"
    "tag object: annotated at $tagCommit (ancestor of origin/main)"
    "dll: $dllBytes bytes, sha256 $dllSha256"
    "package: $packageName, $packageBytes bytes, sha256 $packageSha256"
    "language directories: $($languageDirs.Count)"
    "tests: $testCount"
    "prerelease: $prerelease"
)
$lines | ForEach-Object { Write-Host $_ }

if ($env:GITHUB_STEP_SUMMARY) {
    $summary = @(
        "### Release check: $Tag"
        ''
        '| item | value |'
        '| --- | --- |'
        "| version | $version |"
        "| previous tag | $previousTag |"
        "| package | $packageName |"
        "| package bytes | $packageBytes |"
        "| package sha256 | $packageSha256 |"
        "| dll bytes | $dllBytes |"
        "| dll sha256 | $dllSha256 |"
        "| language directories | $($languageDirs.Count) |"
        "| tests | $testCount |"
        "| prerelease | $prerelease |"
    )
    $summary | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append
}

# The publish steps read these instead of re-deriving anything, so the values
# reported in the summary are the exact ones used for the release.
if ($env:GITHUB_ENV) {
    $envLines = @(
        "RELEASE_TAG=$Tag"
        "RELEASE_VERSION=$version"
        "RELEASE_PACKAGE=$packageName"
        "RELEASE_PACKAGE_BYTES=$packageBytes"
        "RELEASE_PACKAGE_SHA256=$packageSha256"
        "RELEASE_DLL_BYTES=$dllBytes"
        "RELEASE_DLL_SHA256=$dllSha256"
        "RELEASE_PREVIOUS_TAG=$previousTag"
        "RELEASE_TEST_COUNT=$testCount"
        "RELEASE_PRERELEASE=$prerelease"
    )
    $envLines | Out-File -FilePath $env:GITHUB_ENV -Append
}

exit 0
