@echo off
chcp 65001 >nul
title SharpKVM Launcher (Diagnostic Mode)
cd /d "%~dp0"

echo =======================================================
echo  SharpKVM 起動ランチャー (診断・ログ出力モード)
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
    echo [エラー] SharpKVM.exe が見つかりません。
    echo 現在のフォルダ: %CD%
    dir
    goto FINISH
)

echo 実行対象: %TARGET_EXE%
echo 起動中...
echo.

rem 直接フォアグラウンド実行（コンソール出力をそのまま表示）
"%TARGET_EXE%"
set EXIT_CODE=%ERRORLEVEL%

echo.
echo =======================================================
echo プロセス終了 (終了コード: %EXIT_CODE%)
echo =======================================================

if exist "sharpkvm.log" (
    echo.
    echo --- 最新ログ (sharpkvm.log) ---
    type "sharpkvm.log"
    echo -------------------------------
) else (
    echo [注意] sharpkvm.log が生成されませんでした。
    if not "%EXIT_CODE%"=="0" (
        echo アプリケーションが起動する前にOS（Smart App Control / セキュリティ機能）
        echo によって実行が拒否された可能性があります。
    )
)

:FINISH
echo.
echo [確認] このウィンドウは自動で閉じません。
echo 終了するには何かキーを押してください...
pause >nul
