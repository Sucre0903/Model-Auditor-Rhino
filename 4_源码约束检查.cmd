@echo off
setlocal
chcp 65001 >nul
title Model Auditor 0.1.1 - Validate Source
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\ValidateSource.ps1"
set "MODEL_AUDITOR_EXIT=%ERRORLEVEL%"
echo.
pause
exit /b %MODEL_AUDITOR_EXIT%
