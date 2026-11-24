@ECHO OFF
SetLocal EnableExtensions
TITLE APK Shell Extension 2 - Per-user Install
ECHO #######################################################
ECHO ##      APK Shell Extension  2  -  per-user mode     ##
ECHO ##      HKCU registration, no administrator needed   ##
ECHO #######################################################
ECHO.

REM === per-user (HKCU\Software\Classes) registration ===
REM Modern alternative to the machine-wide install.bat: no UAC prompt, applies
REM to the current user only, and is removed again by uninstall_user.bat.
REM Explorer loads shell handlers from the per-user registry view the same way
REM as from HKLM. NGEN is intentionally skipped (it requires admin; the JIT
REM cost only shows on the very first icons after boot).

CD /D "%~dp0"

if not exist "%~dp0ApkShellext2.dll" (
    echo ERROR: ApkShellext2.dll not found next to this script.
    echo Extract the whole archive to the final folder first, then run again.
    pause
    exit /b 1
)

set "REGASM64=%windir%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe"
if not exist "%REGASM64%" (
    echo ERROR: .NET Framework 4.x regasm.exe not found at "%REGASM64%".
    pause
    exit /b 1
)

ECHO [1/5] Generating registration file...
REM random names: fixed %TEMP% file names can be pre-created by another
REM same-user process before we write/import them
set "REGF=%TEMP%\apksext_user_%RANDOM%.reg"
set "REGFH=%TEMP%\apksext_user_hkcu_%RANDOM%.reg"
"%REGASM64%" /codebase /regfile:"%REGF%" "%~dp0ApkShellext2.dll"
if not '%errorlevel%' EQU '0' (
    echo ERROR: regasm /regfile failed with code %errorlevel%.
    pause
    exit /b 1
)

ECHO [2/5] Redirecting HKCR to HKCU\Software\Classes and importing...
powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-Content -LiteralPath '%REGF%' -Raw) -replace 'HKEY_CLASSES_ROOT','HKEY_CURRENT_USER\Software\Classes' | Set-Content -LiteralPath '%REGFH%' -Encoding Unicode"
if not exist "%REGFH%" (
    echo ERROR: PowerShell retarget step failed ^(is powershell.exe available?^).
    pause
    exit /b 1
)
reg import "%REGFH%"
if not '%errorlevel%' EQU '0' (
    echo ERROR: reg import failed with code %errorlevel%.
    pause
    exit /b 1
)
del "%REGF%" "%REGFH%" >nul 2>&1

REM The association layer does not come from the regasm file: /regfile writes
REM only the class keys, and the hook that runs the registrar is what a
REM machine-wide regasm carries but /regfile never executes. ShellRegistrar
REM owns which class serves each extension, the ShellEx slots, the DefaultIcon
REM backup and the WOW6432Node class root mirror a 32-bit host resolves, so the
REM per-user path calls it through the product assembly instead of repeating
REM the layout here.
ECHO [3/5] Registering the shell handlers (per-user)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$null = [Reflection.Assembly]::LoadFrom('%~dp0ApkShellext2.dll'); $code = [ApkShellext2.Registration.RegistrationCommand]::Run('User','%~dp0ApkShellext2.dll'); exit $code"
if not '%errorlevel%' EQU '0' (
    echo ERROR: per-user handler registration failed with code %errorlevel%.
    pause
    exit /b 1
)

ECHO.
ECHO [4/5] Notifying the shell...
ie4uinit.exe -show >nul 2>&1
choice /C YN /M "Restart Explorer now to take effect (recommended)"
if '%errorlevel%' EQU '2' goto verify
taskkill /f /im explorer.exe >nul 2>&1
timeout /t 2 /nobreak >nul
start "" explorer.exe

:verify
ECHO.
ECHO [5/5] Verifying...
reg query "HKCU\SOFTWARE\Classes\CLSID\{1F869CEE-4FDA-35D9-896F-43975A87D1F6}" >nul 2>&1
if '%errorlevel%' EQU '0' (
    echo Per-user install OK: handlers registered under HKCU for this account.
    echo NOTE: if a machine-wide install exists in HKLM, that registration wins.
) else (
    echo WARNING: verification failed - CLSID not found under HKCU after import.
)
ECHO.
PAUSE
exit /b 0
