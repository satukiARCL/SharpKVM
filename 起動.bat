@echo off
title SharpKVM Launcher
cd /d "%~dp0"

echo =======================================================
echo  SharpKVM Diagnostic Launcher
echo =======================================================
echo.

rem 1. Local Development / Smart App Control Bypass mode
if exist "C:\dev\dotnet\dotnet.exe" if exist "bin\Debug\net10.0-windows\SharpKVM.dll" (
    echo [Mode] Using Microsoft-signed dotnet host to bypass Device Guard / Smart App Control...
    echo.
    "C:\dev\dotnet\dotnet.exe" exec --runtimeconfig "bin\Debug\net10.0-windows\SharpKVM.runtimeconfig.json" "bin\Debug\net10.0-windows\SharpKVM.dll"
    goto PROCESS_ENDED
)

rem 2. Standalone folder mode
if exist "SharpKVM.dll" (
    echo [Mode] Standalone directory mode
    dotnet exec SharpKVM.runtimeconfig.json SharpKVM.dll 2>nul
    if %ERRORLEVEL%==0 goto PROCESS_ENDED
)

rem 3. Standalone EXE mode
set TARGET_EXE=
if exist "SharpKVM.exe" (
    set TARGET_EXE=SharpKVM.exe
) else if exist "publish\SharpKVM.exe" (
    set TARGET_EXE=publish\SharpKVM.exe
) else if exist "bin\Debug\net10.0-windows\SharpKVM.exe" (
    set TARGET_EXE=bin\Debug\net10.0-windows\SharpKVM.exe
)

if "%TARGET_EXE%"=="" (
    echo [ERROR] SharpKVM.exe was not found.
    echo Current Directory: %CD%
    dir
    goto FINISH
)

echo Target: %TARGET_EXE%
echo Starting process...
echo.

"%TARGET_EXE%"

:PROCESS_ENDED
set EXIT_CODE=%ERRORLEVEL%

echo.
echo =======================================================
echo Process exited with code: %EXIT_CODE%
echo =======================================================

if exist "sharpkvm.log" (
    echo.
    echo --- Content of sharpkvm.log ---
    type "sharpkvm.log"
    echo -------------------------------
) else (
    echo [NOTE] sharpkvm.log was not generated.
)

:FINISH
echo.
echo Press any key to exit...
pause >nul
