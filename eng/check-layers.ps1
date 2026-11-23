# Fails when the parsing layer reaches into the shell/COM layer.
#
# ApkShellext2\Formats (the package readers and the arsc/axml parsers) and
# ApkShellext2\Imaging (the safe archive and the icon chain) are the testable core: a plain
# console process or NUnit run must be able to load and drive them without explorer, COM
# registration, WinForms or WPF. Everything shell-shaped lives in Shell\, Preferences\ and
# SharpShell\ and is allowed to reference these layers, never the other way around.
#
# The scan is textual and deliberately conservative: it flags a forbidden namespace or type
# token anywhere in the code (comments excluded), so a future `using System.Windows.Forms;`
# fails the gate even when no type from it is used yet. The exact allow list and the
# negative-test recipe are documented in compile_guidance.md section 9.
param(
    [string]$SourceRoot = 'ApkShellext2'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$layers = @('Formats', 'Imaging')

# Token -> human-readable reason. The reader layer must stay a plain .NET library:
# BCL, System.Drawing, the root Utility.cs, Diagnostics\ and Vendor\ are the allowed
# dependencies; the tokens below are the shell/COM/WPF surface that would drag in an
# explorer host (and make the layer untestable outside it).
$forbidden = @(
    @{ Pattern = 'System\.Windows\.';            Reason = 'WinForms/WPF namespace' },
    @{ Pattern = '\bSharpShell\b';               Reason = 'SharpShell shell-extension framework' },
    @{ Pattern = '\bShellExtInitServer\b';       Reason = 'SharpShell server base class' },
    @{ Pattern = '\bServerRegistration\b';       Reason = 'SharpShell registration helper' },
    @{ Pattern = '\bIContextMenu[0-9]?\b';       Reason = 'shell context-menu COM interface' },
    @{ Pattern = '\bIShellExtInit\b';            Reason = 'shell extension COM interface' },
    @{ Pattern = '\bIQueryInfo\b';               Reason = 'shell info-tip COM interface' },
    @{ Pattern = '\bIThumbnailProvider\b';       Reason = 'shell thumbnail COM interface' },
    @{ Pattern = '\bIExtractIconW?\b';           Reason = 'shell icon COM interface' },
    @{ Pattern = '\bIExplorerCommand\b';         Reason = 'Windows 11 shell command interface' },
    @{ Pattern = '\[\s*ComImport\s*\]';          Reason = 'COM interop import' },
    @{ Pattern = '\bComVisible\b';               Reason = 'COM visibility attribute' },
    @{ Pattern = '\[\s*Guid\s*\(';               Reason = 'COM class/interface GUID' }
)

$checked = 0
$violations = New-Object System.Collections.Generic.List[string]

foreach ($layer in $layers) {
    $dir = Join-Path (Join-Path $repoRoot $SourceRoot) $layer
    if (-not (Test-Path $dir)) {
        throw "parsing layer directory is missing: $dir"
    }
    $files = @(Get-ChildItem -Path $dir -Filter *.cs -File -Recurse)
    if ($files.Count -eq 0) {
        throw "parsing layer directory has no C# sources: $dir"
    }
    Write-Host ("{0}: {1} C# files" -f $layer, $files.Count)

    foreach ($file in $files) {
        $checked++
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName -Encoding UTF8) {
            $lineNumber++
            $code = $line.TrimStart()
            # Only whole-line comments are skipped; a token in code is never rescued by a
            # trailing comment, which keeps the check independent of comment formatting.
            if ($code -eq '' -or $code.StartsWith('//') -or $code.StartsWith('/*') -or $code.StartsWith('*')) {
                continue
            }
            foreach ($rule in $forbidden) {
                if ($code -match $rule.Pattern) {
                    $relative = $file.FullName.Substring($repoRoot.Length + 1)
                    $violations.Add(("{0}:{1}: {2}  [{3}]" -f $relative, $lineNumber, $line.Trim(), $rule.Reason))
                    break
                }
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host 'parsing layer must not reference the shell/COM layer:'
    foreach ($violation in $violations) {
        Write-Host ("  " + $violation)
    }
    throw ("{0} forbidden reference(s) in {1} parsed C# file(s)" -f $violations.Count, $checked)
}

Write-Host ("layers OK: {0} C# files under {1}, no shell/COM/WinForms/WPF references" -f $checked, ($layers -join ', '))
