@echo off
title SharpKVM Launcher
cd /d "%~dp0"

echo =======================================================
echo  SharpKVM Diagnostic Launcher
echo =======================================================
echo.

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
