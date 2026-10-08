@echo off
rem Build fm_server.exe and fm_client.exe (x64, static CRT) into bin\.
setlocal

set "ROOT=%~dp0"
set "SRC=%ROOT%src"
set "GEN=%ROOT%build\gen"
set "OBJ=%ROOT%build\obj"
set "BIN=%ROOT%bin"

set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
for /f "usebackq delims=" %%i in (`vswhere.exe -latest -products * -property installationPath`) do set "VSDIR=%%i"
if not defined VSDIR (
    echo Visual Studio / Build Tools not found
    exit /b 1
)
call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

for %%d in ("%GEN%" "%OBJ%\stubs" "%OBJ%\common" "%OBJ%\server" "%OBJ%\client" "%BIN%") do (
    if not exist %%d mkdir %%d
)

echo === MIDL ===
midl /nologo /W1 /env x64 /out "%GEN%" "%SRC%\idl\FileManager.idl" || exit /b 1

set CFLAGS=/nologo /utf-8 /W4 /O2 /MT /DUNICODE /D_UNICODE /D_WIN32_WINNT=0x0A00 /I"%GEN%" /I"%SRC%\common"
set LIBS=rpcrt4.lib advapi32.lib

echo === stubs ===
cl /nologo /W1 /O2 /MT /D_WIN32_WINNT=0x0A00 /I"%GEN%" /c /Fo"%OBJ%\stubs\\" "%GEN%\FileManager_s.c" "%GEN%\FileManager_c.c" || exit /b 1

echo === common ===
cl %CFLAGS% /c /Fo"%OBJ%\common\\" "%SRC%\common\*.c" || exit /b 1

echo === fm_server ===
cl %CFLAGS% /c /Fo"%OBJ%\server\\" "%SRC%\server\*.c" || exit /b 1
link /nologo /OUT:"%BIN%\fm_server.exe" "%OBJ%\server\*.obj" "%OBJ%\common\*.obj" "%OBJ%\stubs\FileManager_s.obj" %LIBS% || exit /b 1

echo === fm_client ===
cl %CFLAGS% /c /Fo"%OBJ%\client\\" "%SRC%\client\*.c" || exit /b 1
link /nologo /OUT:"%BIN%\fm_client.exe" "%OBJ%\client\*.obj" "%OBJ%\common\*.obj" "%OBJ%\stubs\FileManager_c.obj" %LIBS% || exit /b 1

echo.
echo Done: %BIN%
