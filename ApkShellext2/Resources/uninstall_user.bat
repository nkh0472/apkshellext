@ECHO OFF
SetLocal EnableExtensions EnableDelayedExpansion
TITLE APK Shell Extension 2 - Per-user Uninstall
ECHO #######################################################
ECHO ##    APK Shell Extension  2  -  per-user removal    ##
ECHO #######################################################
ECHO.

REM === removes what install_user.bat registered (HKCU\Software\Classes only) ===
REM No administrator privilege needed. Does not touch HKLM machine-wide
REM registrations - use uninstall.bat for those.

CD /D "%~dp0"

REM ShellRegistrar wrote the associations, the class keys, the DefaultIcon backup
REM and the WOW6432Node mirror, so it removes them again: only slots that still
REM point at our class ids, only a backup it took, only a file class it invented.
REM Passing no assembly path keeps this working after the dll is gone.
ECHO [1/3] Removing the registered handlers (per-user)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "if (Test-Path -LiteralPath '%~dp0ApkShellext2.dll') { $null = [Reflection.Assembly]::LoadFrom('%~dp0ApkShellext2.dll') }; exit [ApkShellext2.Registration.RegistrationCommand]::Remove('User')"
if not '%errorlevel%' EQU '0' (
    echo WARNING: the registrar could not remove every key ^(code %errorlevel%^).
    echo Removing the class keys here instead, then continuing with the legacy sweep.
    for %%G in (
        {1F869CEE-4FDA-35D9-896F-43975A87D1F6}
        {D747C5A7-2F66-4B7D-8301-8531838E4ED3}
        {DCB629FC-F86F-456F-8E24-98B9B2643A9B}
        {946435A5-FE96-416D-99DB-E94EE9FB46C8}
    ) do (
        reg delete "HKCU\SOFTWARE\Classes\CLSID\%%G" /f >nul 2>&1
        reg delete "HKCU\SOFTWARE\Classes\Wow6432Node\CLSID\%%G" /f >nul 2>&1
    )
    for %%P in (ApkContextMenu ApkIconHandler ApkInfoTipHandler ApkThumbnailHandler) do reg delete "HKCU\SOFTWARE\Classes\ApkShellext2.%%P" /f >nul 2>&1
)

reg delete "HKCU\SOFTWARE\Classes\ApkShellext2.ApkFile" /f >nul 2>&1

REM Any remaining per-user CLSID whose data references our DLL (covers legacy
REM dependency COM entries created by a pre-3.4.2 install_user.bat).
for /f "delims=" %%K in ('reg query "HKCU\SOFTWARE\Classes\CLSID" /s /f "ApkShellext2.dll" /d /k 2^>nul') do (
    reg delete "%%K" /f >nul 2>&1
    set "KP=%%K"
    set "KP=!KP:\InprocServer32=!"
    reg delete "!KP!" /f >nul 2>&1
)
REM Per-user HKCR\Record mirror entries owned by this assembly.
powershell -NoProfile -ExecutionPolicy Bypass -Command "if (Test-Path 'HKCU:\Software\Classes\Record') { Get-ChildItem 'HKCU:\Software\Classes\Record' | ForEach-Object { $g=$_; if (Get-ChildItem $g.PSPath | Get-ItemProperty -Name Assembly -ErrorAction SilentlyContinue | Where-Object { $_.Assembly -like 'ApkShellext2*' }) { Remove-Item $g.PSPath -Recurse -Force } } }" >nul 2>&1

ECHO.
ECHO [2/3] Verifying...
set "LEFTOVER="
for %%G in (
    {1F869CEE-4FDA-35D9-896F-43975A87D1F6}
    {D747C5A7-2F66-4B7D-8301-8531838E4ED3}
    {DCB629FC-F86F-456F-8E24-98B9B2643A9B}
    {946435A5-FE96-416D-99DB-E94EE9FB46C8}
) do reg query "HKCU\SOFTWARE\Classes\CLSID\%%G" >nul 2>&1 && set "LEFTOVER=1"
if defined LEFTOVER (
    echo WARNING: some per-user CLSID entries are still present.
) else (
    echo All per-user ApkShellext2 registrations removed.
)

ECHO.
ECHO [3/3] Restart Explorer now (recommended)
choice /C YN /M "Restart Explorer now (recommended)"
if '%errorlevel%' EQU '2' goto done
taskkill /f /im explorer.exe >nul 2>&1
timeout /t 2 /nobreak >nul
start "" explorer.exe

:done
ECHO.
PAUSE
exit /b 0
