@echo off
title SharpKVM
cd /d "%~dp0"
if exist "SharpKVM.exe" (
    start "" "SharpKVM.exe"
) else if exist "publish\SharpKVM.exe" (
    start "" "publish\SharpKVM.exe"
) else (
    start "" "bin\Debug\net10.0-windows\SharpKVM.exe"
)
