@echo off
setlocal
cd /d "%~dp0"
if exist ClaudeGlow.exe del /f /q ClaudeGlow.exe >nul 2>&1
if exist ClaudeGlow.exe (
    if exist ClaudeGlow.old.exe del /f /q ClaudeGlow.old.exe >nul 2>&1
    ren ClaudeGlow.exe ClaudeGlow.old.exe
)
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /out:ClaudeGlow.exe /win32manifest:src\app.manifest /win32icon:src\app.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll src\*.cs
if errorlevel 1 exit /b 1
copy /y src\app.config ClaudeGlow.exe.config >nul
if /i not "%1"=="nostart" start "" "%~dp0ClaudeGlow.exe"
