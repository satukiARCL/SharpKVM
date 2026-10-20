@echo off
title SharpKVM Launcher
cd /d "%~dp0"
start "" "C:\dev\dotnet\dotnet.exe" exec --runtimeconfig "bin\Debug\net10.0-windows\SharpKVM.runtimeconfig.json" "bin\Debug\net10.0-windows\SharpKVM.dll"
