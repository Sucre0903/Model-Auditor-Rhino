@echo off
setlocal
chcp 65001 >nul
title Model Auditor - Build Only
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\BuildAndInstall.ps1" -BuildOnly
set "MODEL_AUDITOR_EXIT=%ERRORLEVEL%"
echo.
if not "%MODEL_AUDITOR_EXIT%"=="0" echo 构建失败，请查看上方错误。
pause
exit /b %MODEL_AUDITOR_EXIT%
