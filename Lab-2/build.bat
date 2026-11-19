@echo off
rem Build BackupService.exe and the demo program hello.exe into bin\.
rem Only the .NET Framework 4.x compiler (part of Windows) is required.
setlocal

set "ROOT=%~dp0"
set "SRC=%ROOT%src"
set "BIN=%ROOT%bin"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo .NET Framework 4.x compiler csc.exe not found
    exit /b 1
)

if not exist "%BIN%" mkdir "%BIN%"

set CSFLAGS=/nologo /codepage:65001 /optimize+ /warn:4 /platform:anycpu

echo === BackupService ===
"%CSC%" %CSFLAGS% /target:exe /out:"%BIN%\BackupService.exe" /r:System.IO.Compression.dll "%SRC%\*.cs" || exit /b 1

echo === hello (demo) ===
"%CSC%" %CSFLAGS% /target:winexe /out:"%BIN%\hello.exe" /r:System.Windows.Forms.dll "%ROOT%demo\hello.cs" || exit /b 1

rem The config is copied only once so that edits in bin\ are not overwritten by a rebuild
if not exist "%BIN%\BackupService.ini" copy /y "%ROOT%config\BackupService.ini" "%BIN%\" >nul

echo.
echo Done: %BIN%
