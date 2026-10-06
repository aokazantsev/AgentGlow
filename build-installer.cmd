@echo off
setlocal
cd /d "%~dp0"
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set PAYLOAD=installer\obj\payload.zip
set OUTDIR=%~dp0dist

call "%~dp0build.cmd" nostart
if errorlevel 1 exit /b 1

%CSC% /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /out:Uninstall.exe /win32manifest:installer\uninstall.manifest /win32icon:src\app.ico /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll installer\common\*.cs installer\uninstall\*.cs src\ClaudeHooks.cs src\JsonText.cs src\UserDataPaths.cs
if errorlevel 1 exit /b 1

powershell.exe -NoProfile -ExecutionPolicy Bypass -File installer\make-payload.ps1 -Root "%~dp0." -Output "%~dp0%PAYLOAD%"
if errorlevel 1 exit /b 1

if not exist "%OUTDIR%" mkdir "%OUTDIR%"
%CSC% /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /out:"%OUTDIR%\ClaudeGlowSetup.exe" /win32manifest:installer\setup.manifest /win32icon:src\app.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /resource:%PAYLOAD%,ClaudeGlow.Payload.zip installer\common\*.cs installer\setup\*.cs src\ClaudeHooks.cs src\JsonText.cs src\UserDataPaths.cs
if errorlevel 1 exit /b 1
echo Installer: %OUTDIR%\ClaudeGlowSetup.exe
