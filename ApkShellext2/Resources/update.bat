@ECHO OFF
SetLocal EnableExtensions
TITLE APK Shell Extension 2 - Update
ECHO #######################################################
ECHO ##            APK Shell Extension  2                 ##
ECHO ##      https://github.com/nkh0472/apkshellext       ##
ECHO #######################################################
ECHO.

REM === update.bat: copy a NEW package over the already-registered install ===
REM Run it from the folder that holds the new package (extracted anywhere):
REM   update.bat [/quiet] [/ngen] [/rollback]
REM     /quiet     never PAUSE (scripted use); exit 0 = update OK, 1 = failure
REM     /ngen      also generate NGEN native images (64+32; failures WARN only)
REM     /rollback  restore the newest backup-* folder in the registered install
REM                folder, restart Explorer and print the version (same exit 0/1)
REM
REM The registered install folder is read from the COM CodeBase value (HKLM
REM 64-bit view, then HKLM WOW6432Node, then HKCU - machine wins, matching the
REM install scripts). The new files are staged, the old eight product files are
REM moved to backup-<oldversion>-<timestamp> together with the files this version
REM no longer ships (ExCSS.dll and the four Span facade assemblies, so an upgrade
REM from 3.5.1 leaves no stale private assembly next to the DLL), the new files
REM are copied in and the SAME registration scope is repaired in place. Nothing
REM is pushed to the registry that points at this package folder itself.
REM
REM update.bat is part of the package payload. A migration update (package
REM extracted anywhere) moves the installed copy into the backup folder, so
REM /rollback restores the file set that was there before the update. An
REM in-place update (package extracted over the install folder) leaves the
REM running script alone while this window reads it; /rollback then deletes it,
REM because the backup holds no update.bat - the case for every install made
REM before update.bat shipped.
REM
REM The final step reads every handler slot back from the file class: a slot is
REM a subkey whose default value is the class id, and only that shape is loaded.
REM
REM Registration note: install*.bat and this script each carry their own regasm /
REM HKCU association logic and must stay byte-for-byte equivalent for the same
REM registration outcome. Never simplify one of them alone.

CD /D "%~dp0"
set "SCRIPTDIR=%~dp0"
if "%SCRIPTDIR:~-1%"=="\" set "SCRIPTDIR=%SCRIPTDIR:~0,-1%"
set "CLSID={1F869CEE-4FDA-35D9-896F-43975A87D1F6}"
set "K64=HKLM\SOFTWARE\Classes\CLSID\%CLSID%\InprocServer32"
set "K32=HKLM\SOFTWARE\WOW6432Node\Classes\CLSID\%CLSID%\InprocServer32"
set "KU=HKCU\SOFTWARE\Classes\CLSID\%CLSID%\InprocServer32"
set "BLOCK64=HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked"
set "BLOCK32=HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked"
set "BLOCKU=HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked"
set "PRODUCT=ApkShellext2.dll install.bat install_user.bat uninstall.bat uninstall_user.bat restart_explorer.bat libwebp_x86.dll libwebp_x64.dll"
REM Files that packages up to 3.5.1 deployed next to the DLL and 3.6.0 no longer
REM ships: the ExCSS parser and the four Span facade assemblies. They are moved
REM into the backup folder (never deleted) so an upgrade from 3.5.1 does not leave
REM a second, stale copy of a private assembly next to the new build, and
REM /rollback puts them back.
set "LEGACY=ExCSS.dll System.Buffers.dll System.Memory.dll System.Numerics.Vectors.dll System.Runtime.CompilerServices.Unsafe.dll"
set "PAYLOAD=%PRODUCT% update.bat"
set "LANGS=ar-SA de-DE el-GR es-ES fa-IR fr-FR he-IL it-IT ja-JP ko-KR my-MM nl-NL pl-PL pt-PT ru-RU tr-TR uk-UA zh-CN zh-TW"
set "QUIET="
set "NGEN="
set "ROLLBACK="
set "FAILMSG="
set "KILLED="
set "BACKUP="
set "STAGE="
if not "%~1"=="" for %%A in (%*) do call :take_arg "%%~A"

REM === 1) where is the extension installed, and in which scope? ===
REM A bare query would see only one registry view because update.bat may be
REM launched from a 32-bit shell; the two HKLM paths are spelled out explicitly.
set "SCOPE="
set "KEY="
set "TARGET="
call :try_install "%K64%" machine
call :try_install "%K32%" machine
call :try_install "%KU%" user
if not defined TARGET goto :die_noinstall

REM === elevation: only a machine-wide registration needs administrator ===
REM elevation goes through a 64-bit cscript (Sysnative alias when the caller is
REM a 32-bit process): 32-bit ShellExecuteEx "runas" is broken on Windows 10/11,
REM see kkguo/apkshellext#116. User-scope installations never elevate.
>nul 2>&1 net session
if '%errorlevel%' EQU '0' goto gotAdmin
if /I not "%SCOPE%"=="machine" goto gotAdmin
if "%~1"=="1" goto :die_noadmin

:UACPrompt
echo Requesting administrative privileges...
set "CSCRIPT=%SystemRoot%\System32\cscript.exe"
if exist "%SystemRoot%\Sysnative\cscript.exe" set "CSCRIPT=%SystemRoot%\Sysnative\cscript.exe"
set "ELEV_ARGS=1"
if defined QUIET set "ELEV_ARGS=%ELEV_ARGS% /quiet"
if defined NGEN set "ELEV_ARGS=%ELEV_ARGS% /ngen"
if defined ROLLBACK set "ELEV_ARGS=%ELEV_ARGS% /rollback"
set "ELEV_VBS=%temp%\apkshellext_update_%RANDOM%.vbs"
echo CreateObject("Shell.Application").ShellExecute "%~f0", "%ELEV_ARGS%", "", "runas" > "%ELEV_VBS%"
"%CSCRIPT%" //nologo "%ELEV_VBS%"
del "%ELEV_VBS%" 2>nul
echo The elevated update runs in its own window; this one exits now.
echo Wait for it to finish, then run: reg query "%KEY%" /v CodeBase
exit /b 0

:die_noadmin
set "FAILMSG=Cannot elevate administrator privilege. Right-click update.bat and choose Run as administrator."
goto :failmsg

:gotAdmin
if defined ROLLBACK goto :rollback

if not exist "%SCRIPTDIR%\ApkShellext2.dll" (
    echo ERROR: ApkShellext2.dll not found next to update.bat.
    echo Extract the whole new package into one folder, then run update.bat from it.
    goto :failmsg
)
if not exist "%TARGET%\ApkShellext2.dll" (
    echo ERROR: the registered install folder has no ApkShellext2.dll:
    echo     "%TARGET%"
    echo Run install.bat or install_user.bat in that folder again to repair it.
    goto :failmsg
)

REM === in-place (A) vs migration (B) ===
REM The on-disk dll is already new in mode A (the package was extracted over the
REM registered folder), so only the registered Assembly version survives there;
REM mode B still sees the old dll and reads FileVersionInfo. Trailing slashes and
REM letter case are ignored because both sides come from different sources.
call :detect_mode
if not defined MODE (
    set "FAILMSG=could not compare the registered folder with this folder; run update.bat from the extracted package again"
    goto :failmsg
)

ECHO.
ECHO [1/7] Registered installation:
ECHO     scope:  %SCOPE%
ECHO     folder: %TARGET%
if /I "%MODE%"=="A" (
    ECHO     mode:   A - in-place, this package was extracted over it
) else (
    ECHO     mode:   B - migration, copying this package into the registered folder
)
if /I "%SCOPE%"=="machine" if /I "%MODE%"=="B" (
    ECHO     note:   machine-wide install - updating requires administrator rights
)
REM === 2) stage the new package BEFORE anything is moved ===
REM The package folder may be the install folder itself (mode A); moving files
REM first would delete the very files that still have to be copied.
ECHO.
ECHO [2/7] Staging the new package...
set "STAGE=%TEMP%\apks_update_%RANDOM%%RANDOM%"
mkdir "%STAGE%" >nul 2>&1
if not exist "%STAGE%" (
    set "FAILMSG=could not create a staging folder in %TEMP%; check free space and permissions"
    goto :failmsg
)
for %%F in (%PAYLOAD%) do (
    if not exist "%SCRIPTDIR%\%%F" (
        set "FAILMSG=the new package is incomplete: %%F is missing next to update.bat"
        goto :failmsg
    )
    copy /y "%SCRIPTDIR%\%%F" "%STAGE%\%%F" >nul 2>&1
    if errorlevel 1 (
        set "FAILMSG=could not copy %%F into the staging folder in %TEMP%"
        goto :failmsg
    )
)
for %%L in (%LANGS%) do (
    xcopy /e /i /y /r /h "%SCRIPTDIR%\%%L" "%STAGE%\%%L\" >nul 2>&1
    if errorlevel 1 (
        set "FAILMSG=could not stage language folder %%L from the new package"
        goto :failmsg
    )
)
ECHO     staged 9 root files + 19 language folders in %TEMP%

REM === 3) release the locks ===
REM Killing Explorer is not enough: Explorer hands shell extensions to dllhost
REM (thumbnail / preview surrogate) and those hosts keep the DLL mapped. Kill
REM ONLY dllhost processes that actually have our module loaded; never all of
REM them, or unrelated shell previews die for no reason.
ECHO.
ECHO [3/7] Releasing file locks (Explorer and preview hosts)...
taskkill /f /im explorer.exe >nul 2>&1
set "KILLED=1"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$names=@('ApkShellext2.dll','libwebp_x64.dll','libwebp_x86.dll'); Get-Process dllhost -ErrorAction SilentlyContinue | ForEach-Object { $p=$_; try { $hit=$false; foreach($m in $p.Modules){ if($names -contains $m.ModuleName){ $hit=$true; break } }; if($hit){ Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } } catch {} }"
if errorlevel 1 (
    set "FAILMSG=could not stop the preview host that loaded the extension; close all Explorer windows and retry"
    goto :fail_restart
)
set "PDLL=%TARGET%\ApkShellext2.dll"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$dll=$env:PDLL; $deadline=(Get-Date).AddSeconds(10); while($true){ try { $fs=[IO.File]::Open($dll,'Open','ReadWrite','None'); $fs.Close(); exit 0 } catch { if((Get-Date) -gt $deadline){ exit 1 }; Start-Sleep -Milliseconds 500 } }"
if errorlevel 1 (
    set "FAILMSG=ApkShellext2.dll is still locked after 10 seconds; close open Explorer windows and the preview pane, then run update.bat again"
    goto :fail_restart
)
ECHO     file locks released

REM === 4) backup ===
REM The eight product files are moved into the backup folder. update.bat joins
REM them in migration mode only: there it is not the script that is running, so
REM /rollback can put the previous package's update.bat back. In in-place mode
REM the running script is the install folder copy and stays where it is.
ECHO.
ECHO [4/7] Backing up the current installation...
call :read_old_version
for /f "usebackq delims=" %%S in (`powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"`) do set "STAMP=%%S"
if not defined STAMP set "STAMP=19700101-000000"
set "BACKUP=%TARGET%\backup-%OLDVER%-%STAMP%"
mkdir "%BACKUP%" >nul 2>&1
if not exist "%BACKUP%" (
    set "FAILMSG=could not create the backup folder %BACKUP%; check write access to the install folder"
    goto :fail_restart
)
for %%F in (%PRODUCT%) do (
    if exist "%TARGET%\%%F" (
        move /y "%TARGET%\%%F" "%BACKUP%\%%F" >nul 2>&1
        if errorlevel 1 (
            set "FAILMSG=could not move %%F into the backup folder; close any program using it and retry"
            goto :fail_rollback
        )
    )
)
set "OBSOLETE=0"
for %%F in (%LEGACY%) do (
    if exist "%TARGET%\%%F" (
        move /y "%TARGET%\%%F" "%BACKUP%\%%F" >nul 2>&1
        if errorlevel 1 (
            set "FAILMSG=could not move the obsolete %%F into the backup folder; close any program using it and retry"
            goto :fail_rollback
        )
        set /a OBSOLETE+=1
    )
)
REM The previous package's update.bat belongs to the old file set as well, but
REM only a migration update can move it: in-place mode is reading this file.
if /I not "%MODE%"=="A" (
    if exist "%TARGET%\update.bat" (
        move /y "%TARGET%\update.bat" "%BACKUP%\update.bat" >nul 2>&1
        if errorlevel 1 (
            set "FAILMSG=could not move the previous update.bat into the backup folder; close any program using it and retry"
            goto :fail_rollback
        )
    )
)
ECHO     backup folder: %BACKUP%
if not "%OBSOLETE%"=="0" ECHO     moved %OBSOLETE% file(s) this package no longer ships out of the install folder

REM === 5) copy + register ===
ECHO.
ECHO [5/7] Copying the new files and repairing registration...
if /I "%MODE%"=="A" (
    REM A running .bat cannot be overwritten; mode A already has the new update.bat
    for %%F in (%PRODUCT%) do (
        xcopy /y /r /h "%STAGE%\%%F" "%TARGET%\" >nul 2>&1
        if errorlevel 1 (
            set "FAILMSG=could not copy %%F into the install folder; check write access"
            goto :fail_rollback
        )
    )
) else (
    for %%F in (%PAYLOAD%) do (
        xcopy /y /r /h "%STAGE%\%%F" "%TARGET%\" >nul 2>&1
        if errorlevel 1 (
            set "FAILMSG=could not copy %%F into the install folder; check write access"
            goto :fail_rollback
        )
    )
)
for %%L in (%LANGS%) do (
    xcopy /e /i /y /r /h "%STAGE%\%%L" "%TARGET%\%%L\" >nul 2>&1
    if errorlevel 1 (
        set "FAILMSG=could not copy language folder %%L into the install folder"
        goto :fail_rollback
    )
)
ECHO     new files copied

set "REGASM64=%windir%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe"
set "REGASM32=%windir%\Microsoft.NET\Framework\v4.0.30319\regasm.exe"
if not exist "%REGASM64%" (
    set "FAILMSG=.NET Framework 4.x regasm.exe was not found at %windir%\Microsoft.NET\Framework64\v4.0.30319; install .NET Framework 4.x and retry"
    goto :fail_rollback
)

if /I "%SCOPE%"=="machine" (
    call :register_machine
    if defined FAILMSG goto :fail_rollback
) else (
    call :register_user
    if defined FAILMSG goto :fail_rollback
)

REM The blocked list is what makes a menu or icon vanish "for no reason" after
REM Explorer crashes: it survives re-installation, so an
REM update that does not clear it looks broken forever. All four CLSIDs, both
REM machine views and the per-user list.
ECHO     Clearing the Windows shell-extension block list...
for %%G in (
    {1F869CEE-4FDA-35D9-896F-43975A87D1F6}
    {D747C5A7-2F66-4B7D-8301-8531838E4ED3}
    {DCB629FC-F86F-456F-8E24-98B9B2643A9B}
    {946435A5-FE96-416D-99DB-E94EE9FB46C8}
) do (
    reg delete "%BLOCK64%" /v %%G /f >nul 2>&1
    reg delete "%BLOCK32%" /v %%G /f >nul 2>&1
    reg delete "%BLOCKU%" /v %%G /f >nul 2>&1
)
reg query "%BLOCK64%" /v %CLSID% >nul 2>&1 && echo WARNING: a machine-wide Blocked entry remains; run update.bat from an elevated prompt to clear it.

set "NGEN64=%windir%\Microsoft.NET\Framework64\v4.0.30319\ngen.exe"
set "NGEN32=%windir%\Microsoft.NET\Framework\v4.0.30319\ngen.exe"
if defined NGEN (
    ECHO     Generating NGEN native images...
    if exist "%NGEN64%" (
        "%NGEN64%" install "%TARGET%\ApkShellext2.dll" /nologo /NoDependencies
        if errorlevel 1 echo WARNING: 64-bit NGEN failed - service disabled? icons still work.
    )
    if exist "%NGEN32%" (
        "%NGEN32%" install "%TARGET%\ApkShellext2.dll" /nologo /NoDependencies
        if errorlevel 1 echo WARNING: 32-bit NGEN failed; 64-bit hosts still work.
    )
) else (
    ECHO     NGEN skipped - pass /ngen to generate native images
)

ECHO.
ECHO [6/7] Notifying the shell and restarting Explorer...
ie4uinit.exe -show >nul 2>&1
REM A second taskkill is harmless: step 3 already stopped the old shell, and a
REM user may have reopened a window while the files were being copied.
taskkill /f /im explorer.exe >nul 2>&1
timeout /t 2 /nobreak >nul 2>&1
start "" explorer.exe >nul 2>&1
ECHO     Explorer restarted

ECHO.
ECHO [7/7] Verifying...
set "CB="
for /f "tokens=2,*" %%A in ('reg query "%KEY%" /v CodeBase 2^>nul ^| findstr /i "CodeBase"') do set "CB=%%B"
if not defined CB (
    set "FAILMSG=CodeBase is missing after registration; run install.bat or install_user.bat in the install folder to repair it"
    goto :failmsg
)
if /I "%CB:~0,8%"=="file:///" set "CB=%CB:~8%"
if /I "%CB:~0,7%"=="file://" set "CB=%CB:~7%"
set "CB=%CB:/=\%"
for %%D in ("%CB%") do set "CB=%%~dpD"
if "%CB:~-1%"=="\" set "CB=%CB:~0,-1%"
set "C1=%CB%"
set "C2=%TARGET%"
set "CMP="
for /f "usebackq delims=" %%M in (`powershell -NoProfile -Command "if (($env:C1).TrimEnd([char]92) -ieq ($env:C2).TrimEnd([char]92)) { 'OK' } else { 'BAD' }"`) do set "CMP=%%M"
if /I not "%CMP%"=="OK" (
    ECHO     WARNING: CodeBase still points at "%CB%"
    ECHO     Expected "%TARGET%". Run uninstall.bat from the old folder, then retry.
) else (
    ECHO     CodeBase: %CB%
)
REM The registration is only as good as what the shell reads back: every handler
REM slot is a subkey whose default value is the class id of this package, under
REM the file class each extension resolves to. A missing or foreign value there
REM means Explorer will not load the handler, so it is an error, not a warning.
if /I "%SCOPE%"=="machine" (
    set "VERIFYROOTS=HKLM\SOFTWARE\Classes HKLM\SOFTWARE\WOW6432Node\Classes"
) else (
    set "VERIFYROOTS=HKCU\SOFTWARE\Classes HKCU\SOFTWARE\Classes\Wow6432Node"
)
set "SLOTFAIL="
call :verify_slots
call :print_version
if defined SLOTFAIL (
    ECHO.
    ECHO ERROR: the handler slots read back above do not match this package, so Explorer will not load the handlers.
    ECHO Repair it by running install.bat or install_user.bat in "%TARGET%" again, then run update.bat again.
    if defined STAGE if exist "%STAGE%" rmdir /s /q "%STAGE%" >nul 2>&1
    if not defined QUIET pause
    exit /b 1
)
ECHO.
ECHO Update finished. If icons still look stale, close and reopen Explorer windows.
if not defined QUIET pause
if defined STAGE if exist "%STAGE%" rmdir /s /q "%STAGE%" >nul 2>&1
exit /b 0

REM ---- subroutines -------------------------------------------------------

:take_arg
set "ARG=%~1"
if /I "%ARG%"=="/quiet" set "QUIET=1"
if /I "%ARG%"=="/ngen" set "NGEN=1"
if /I "%ARG%"=="/rollback" set "ROLLBACK=1"
goto :eof

:detect_mode
REM A = this package is the install folder itself, B = it has to be copied over
set "MODE="
set "T1=%TARGET%"
set "T2=%SCRIPTDIR%"
for /f "usebackq delims=" %%M in (`powershell -NoProfile -Command "if (($env:T1).TrimEnd([char]92) -ieq ($env:T2).TrimEnd([char]92)) { 'A' } else { 'B' }"`) do set "MODE=%%M"
goto :eof

:try_install
REM %1 = InprocServer32 key, %2 = scope name; first hit wins (machine first)
if defined TARGET goto :eof
call :read_cb "%~1" CB
if not defined CB goto :eof
for %%D in ("%CB%") do set "TARGET=%%~dpD"
if not defined TARGET goto :eof
if "%TARGET:~-1%"=="\" set "TARGET=%TARGET:~0,-1%"
set "SCOPE=%~2"
set "KEY=%~1"
goto :eof

:read_cb
REM %1 = InprocServer32 key, %2 = variable that receives the CodeBase dll path
set "CB2="
for /f "tokens=2,*" %%A in ('reg query "%~1" /v CodeBase 2^>nul ^| findstr /i "CodeBase"') do set "CB2=%%B"
if not defined CB2 goto :read_cb_empty
REM regasm /codebase writes file:///C:/... on some machines; a plain path is
REM needed for if-exist and for the folder comparison at the end.
if /I not "%CB2:~0,8%"=="file:///" goto :read_cb_nofile
set "CB2=%CB2:~8%"
:read_cb_nofile
if /I not "%CB2:~0,7%"=="file://" goto :read_cb_noscheme
set "CB2=%CB2:~7%"
:read_cb_noscheme
set "CB2=%CB2:/=\%"
set "%~2=%CB2%"
goto :eof
:read_cb_empty
set "%~2="
goto :eof

:read_old_version
REM Mode A already has the new dll on disk, so only the registered Assembly
REM version still knows the old build; mode B can read the old dll directly.
set "OLDVER="
if /I "%MODE%"=="A" call :read_assembly_version
if defined OLDVER goto :eof
call :read_file_version
if not defined OLDVER set "OLDVER=unknown"
goto :eof

:read_assembly_version
set "ASMV="
for /f "tokens=2,*" %%A in ('reg query "%KEY%" /v Assembly 2^>nul ^| findstr /i "Assembly"') do set "ASMV=%%B"
if not defined ASMV goto :eof
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "if ($env:ASMV -match 'Version=([0-9.]+)') { $matches[1] }"`) do set "OLDVER=%%V"
goto :eof

:read_file_version
set "PDLL=%TARGET%\ApkShellext2.dll"
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "try { [Diagnostics.FileVersionInfo]::GetVersionInfo($env:PDLL).FileVersion } catch { }"`) do set "OLDVER=%%V"
goto :eof

:print_version
set "PDLL=%TARGET%\ApkShellext2.dll"
set "NEWVER="
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "try { [Diagnostics.FileVersionInfo]::GetVersionInfo($env:PDLL).FileVersion } catch { }"`) do set "NEWVER=%%V"
if not defined NEWVER set "NEWVER=unknown"
ECHO     ApkShellext2.dll FileVersion: %NEWVER%
ECHO     install folder: %TARGET%
goto :eof

:verify_slots
REM Every extension in every class root of the registration scope: 8 icon slots,
REM info-tip and context menu for the seven extensions that have them, and the
REM thumbnail slot for .apk - 23 slots per root, 46 for a two-root scope. The
REM ids mirror ApkShellext2\Registration\ShellGuids.cs and install_user.bat.
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

:restore_backup
if not defined BACKUP goto :eof
if not exist "%BACKUP%" goto :eof
for %%F in (%PRODUCT% %LEGACY%) do if exist "%BACKUP%\%%F" move /y "%BACKUP%\%%F" "%TARGET%\%%F" >nul 2>&1
call :restore_updater
rmdir "%BACKUP%" >nul 2>&1
set "RESTORED=1"
goto :eof

:restore_updater
REM A migration update moves the installed update.bat into the backup folder, so
REM /rollback restores it and the file set is exactly what was there before. An
REM install that predates update.bat has no such file in the backup, so the copy
REM this update placed in the install folder has to go as well.
if not defined BACKUP goto :eof
if exist "%BACKUP%\update.bat" (
    move /y "%BACKUP%\update.bat" "%TARGET%\update.bat" >nul 2>&1
    goto :eof
)
if not exist "%TARGET%\update.bat" goto :eof
if /I not "%MODE%"=="A" (
    del /f /q "%TARGET%\update.bat" >nul 2>&1
    goto :eof
)
REM In-place mode has no file to delete while this window reads it: the caller
REM ends the script with the deletion (see :rollback).
set "UPDATER_SELFDEL=1"
goto :eof

:latest_backup
set "LATEST="
for /f "delims=" %%D in ('dir /b /ad /o-d "%TARGET%\backup-*" 2^>nul') do if not defined LATEST set "LATEST=%%D"
goto :eof

:register_machine
REM regasm /codebase runs the native handlers' [ComRegisterFunction], which is
REM the same registrar call the per-user path makes: it owns the class keys,
REM the association slots, the 32-bit class root mirror and the DefaultIcon
REM backup, and it never overwrites a backup that already exists, so repeated
REM updates keep the icon the user had before the first install.
ECHO     Registering shell handlers (64-bit view)...
"%REGASM64%" /codebase "%TARGET%\ApkShellext2.dll"
if errorlevel 1 (
    set "FAILMSG=64-bit regasm /codebase failed; the previous files were restored"
    goto :eof
)
if exist "%REGASM32%" (
    ECHO     Registering shell handlers (32-bit view)...
    "%REGASM32%" /codebase "%TARGET%\ApkShellext2.dll"
    if errorlevel 1 echo WARNING: 32-bit registration failed; 64-bit hosts still work.
)
goto :eof

:register_user
REM Same registration result as install_user.bat: regasm writes an HKCR .reg
REM file, the root is rewritten to HKCU\Software\Classes, the associations are
REM added explicitly, and an existing DefaultIcon backup is never overwritten.
ECHO     Importing the per-user class registration (HKCU)...
set "REGF=%TEMP%\apksext_update_%RANDOM%.reg"
set "REGFH=%TEMP%\apksext_update_hkcu_%RANDOM%.reg"
"%REGASM64%" /codebase /regfile:"%REGF%" "%TARGET%\ApkShellext2.dll" >nul 2>&1
if errorlevel 1 (
    set "FAILMSG=regasm /codebase /regfile failed; the previous files were restored"
    del "%REGF%" >nul 2>&1
    goto :eof
)
if not exist "%REGF%" (
    set "FAILMSG=regasm did not write the registration file; .NET Framework 4.x may be damaged"
    goto :eof
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$c=Get-Content -LiteralPath $env:REGF -Raw; $c=$c -replace 'HKEY_CLASSES_ROOT','HKEY_CURRENT_USER\Software\Classes'; Set-Content -LiteralPath $env:REGFH -Value $c -Encoding Unicode" >nul 2>&1
if errorlevel 1 (
    set "FAILMSG=could not rewrite the registration file for HKCU; PowerShell is unavailable"
    del "%REGF%" >nul 2>&1
    goto :eof
)
reg import "%REGFH%" >nul 2>&1
if errorlevel 1 (
    set "FAILMSG=reg import of the HKCU registration failed; the previous files were restored"
    del "%REGF%" "%REGFH%" >nul 2>&1
    goto :eof
)
del "%REGF%" "%REGFH%" >nul 2>&1

REM The registrar owns the per-user association layer: it writes the same
REM slots the installer always wrote, plus the Wow6432Node class root mirror a
REM 32-bit host resolves, and the DefaultIcon backup lives in one place.
ECHO     Writing the per-user file-type associations (HKCU)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$null = [Reflection.Assembly]::LoadFrom('%TARGET%\ApkShellext2.dll'); $code = [ApkShellext2.Registration.RegistrationCommand]::Run('User','%TARGET%\ApkShellext2.dll'); exit $code" >nul 2>&1
if errorlevel 1 (
    set "FAILMSG=per-user handler registration failed"
    goto :eof
)
ECHO     per-user registration repaired
goto :eof

:rollback
ECHO.
ECHO [1/4] Looking for the newest backup in "%TARGET%"...
call :latest_backup
if not defined LATEST (
    set "FAILMSG=no backup-* folder exists in the registered install folder; /rollback only works after update.bat ran there"
    goto :failmsg
)
call :detect_mode
set "BACKUP=%TARGET%\%LATEST%"
ECHO [2/4] Restoring "%TARGET%\%LATEST%"...
for %%F in (%PRODUCT% %LEGACY%) do (
    if exist "%TARGET%\%LATEST%\%%F" (
        move /y "%TARGET%\%LATEST%\%%F" "%TARGET%\%%F" >nul 2>&1
        if errorlevel 1 (
            set "FAILMSG=could not restore %%F from the backup; close Explorer windows and retry"
            goto :failmsg
        )
    )
)
call :restore_updater
rmdir "%TARGET%\%LATEST%" >nul 2>&1
ECHO [3/4] Restarting Explorer...
taskkill /f /im explorer.exe >nul 2>&1
timeout /t 2 /nobreak >nul 2>&1
start "" explorer.exe >nul 2>&1
ECHO [4/4] Verifying...
call :print_version
ECHO.
ECHO Rollback finished. The restored build keeps the existing registration.
if not defined QUIET pause
if not defined UPDATER_SELFDEL exit /b 0
REM In-place mode: this window is the leftover updater itself. Deleting the file
REM ends the script, so the deletion is the last thing this path does.
(goto) 2>nul & del /f /q "%TARGET%\update.bat"

:die_noinstall
ECHO.
ECHO Not installed yet: run install.bat or install_user.bat first.
if defined STAGE if exist "%STAGE%" rmdir /s /q "%STAGE%" >nul 2>&1
if not defined QUIET pause
exit /b 1

:fail_rollback
call :restore_backup
:fail_restart
if defined KILLED (
    taskkill /f /im explorer.exe >nul 2>&1
    start "" explorer.exe >nul 2>&1
)
:failmsg
if defined STAGE if exist "%STAGE%" rmdir /s /q "%STAGE%" >nul 2>&1
if not defined FAILMSG set "FAILMSG=update failed"
ECHO.
ECHO ERROR: %FAILMSG%
if defined RESTORED (
    ECHO The previous files were moved back from the backup folder.
) else (
    if defined BACKUP (
        ECHO The previous files are kept in: "%BACKUP%"
    ) else (
        ECHO Nothing in the installed copy was changed.
    )
)
if defined UPDATER_SELFDEL start "" /b cmd /c ping -n 3 127.0.0.1 ^>nul ^& del /f /q ^"%TARGET%\update.bat^"
if not defined QUIET pause
exit /b 1
