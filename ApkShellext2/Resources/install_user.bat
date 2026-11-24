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
REM The class ids come from the regasm file, but the shell slots come from the
REM registrar, and a slot in the wrong shape fails silently: the shell reads a
REM slot only as a subkey whose default value is the class id, and ignores a
REM value of the same name on ShellEx. So every slot is read back from the file
REM class of every extension, in both class roots of this scope.
set "VERIFYROOTS=HKCU\SOFTWARE\Classes HKCU\SOFTWARE\Classes\Wow6432Node"
set "SLOTFAIL="
reg query "HKCU\SOFTWARE\Classes\CLSID\{1F869CEE-4FDA-35D9-896F-43975A87D1F6}" >nul 2>&1
if errorlevel 1 (
    ECHO     ERROR: HKCU\SOFTWARE\Classes\CLSID\{1F869CEE-4FDA-35D9-896F-43975A87D1F6} is missing
    ECHO            the shell handler class was not registered for this account
    set "SLOTFAIL=1"
)
call :verify_slots
if defined SLOTFAIL (
    echo WARNING: verification failed - a handler slot above is missing or does not
    echo hold the class id of this package as its default value. Run install_user.bat
    echo again; if that does not help, another application may own the file class.
) else (
    echo Per-user install OK: handlers registered under HKCU for this account.
    echo NOTE: if a machine-wide install exists in HKLM, that registration wins.
)
ECHO.
PAUSE
exit /b 0

:verify_slots
REM Every extension in every class root of the registration scope: 8 icon slots,
REM info-tip and context menu for the seven extensions that have them, and the
REM thumbnail slot for .apk - 23 slots per root, 46 for a two-root scope.
set "SLOTOK=0"
set "SLOTBAD=0"
for %%R in (%VERIFYROOTS%) do for %%E in (.apk .ipa .aab .appx .appxbundle .apks .apkm .xapk) do call :verify_class "%%R" "%%E"
if not "%SLOTBAD%"=="0" set "SLOTFAIL=1"
if "%SLOTBAD%"=="0" ECHO     %SLOTOK% handler slot(s) read back from the file class of all eight extensions
goto :eof

:verify_class
REM %1 = classes root, %2 = extension with the dot. The shell loads a handler
REM from the file class the extension points at (the extension key's default
REM value), never from the extension key itself, so the class is resolved the
REM same way the registrar wrote it - including the '<name>.1' fallback.
set "VC_KEY=%~1\%~2"
set "VC_CLASS="
for /f "tokens=2,*" %%A in ('reg query "%VC_KEY%" /ve 2^>nul ^| findstr /i "REG_SZ"') do set "VC_CLASS=%%B"
if not defined VC_CLASS set "VC_CLASS=%~2"
if "%VC_CLASS:~0,1%"=="." set "VC_CLASS=%VC_CLASS:~1%.1"
call :checkslot "%~1\%VC_CLASS%" "IconHandler" "{1F869CEE-4FDA-35D9-896F-43975A87D1F6}"
if /I not "%~2"==".aab" (
    call :checkslot "%~1\%VC_CLASS%" "{00021500-0000-0000-C000-000000000046}" "{946435A5-FE96-416D-99DB-E94EE9FB46C8}"
    call :checkslot "%~1\%VC_CLASS%" "ContextMenuHandlers\ApkContextMenu" "{DCB629FC-F86F-456F-8E24-98B9B2643A9B}"
)
if /I "%~2"==".apk" call :checkslot "%~1\%VC_CLASS%" "{E357FCCD-A995-4576-B01F-234630154E96}" "{D747C5A7-2F66-4B7D-8301-8531838E4ED3}"
goto :eof

:checkslot
REM %1 = file class key, %2 = subkey below ShellEx, %3 = class id expected there
set "SLOTVAL="
for /f "tokens=2,*" %%a in ('reg query "%~1\ShellEx\%~2" /ve 2^>nul ^| findstr /i "REG_SZ"') do set "SLOTVAL=%%b"
if /i "%SLOTVAL%"=="%~3" (
    set /a SLOTOK+=1
    goto :eof
)
set /a SLOTBAD+=1
if not defined SLOTVAL ECHO     ERROR: %~1\ShellEx\%~2 is missing
if defined SLOTVAL ECHO     ERROR: %~1\ShellEx\%~2 holds "%SLOTVAL%", expected "%~3"
goto :eof
