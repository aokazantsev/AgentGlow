@echo off
setlocal
cd /d "%~dp0"
set APP=ClaudeGlow
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set REFS=/r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll
set SHARED=src\AppIdentity.cs src\Autostart.cs
set UNINSTALL_EXTRA=src\ClaudeHooks.cs src\JsonText.cs
set SETUP_EXTRA=src\ClaudeHooks.cs src\JsonText.cs src\CrashReportConsent.cs
set PREBUILD=
set PAYLOAD=installer\obj\payload.zip
set OUTDIR=%~dp0dist

call "%~dp0build.cmd" nostart
if errorlevel 1 exit /b 1

if not "%PREBUILD%"=="" powershell.exe -NoProfile -ExecutionPolicy Bypass -File %PREBUILD% -Root "%~dp0."
if errorlevel 1 exit /b 1

%CSC% /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /nowarn:0649 /out:Uninstall.exe /win32manifest:installer\uninstall.manifest /win32icon:src\app.ico %REFS% installer\common\*.cs installer\uninstall\*.cs %SHARED% %UNINSTALL_EXTRA%
if errorlevel 1 exit /b 1

powershell.exe -NoProfile -ExecutionPolicy Bypass -File installer\make-payload.ps1 -Root "%~dp0." -Output "%~dp0%PAYLOAD%"
if errorlevel 1 exit /b 1

if not exist "%OUTDIR%" mkdir "%OUTDIR%"
%CSC% /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /out:"%OUTDIR%\%APP%Setup.exe" /win32manifest:installer\setup.manifest /win32icon:src\app.ico %REFS% /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /resource:%PAYLOAD%,%APP%.Payload.zip installer\common\*.cs installer\setup\*.cs %SHARED% %SETUP_EXTRA%
if errorlevel 1 exit /b 1

del /f /q %APP%.exe %APP%.exe.config Uninstall.exe >nul 2>&1
echo Installer: %OUTDIR%\%APP%Setup.exe
