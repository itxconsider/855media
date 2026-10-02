@echo off
title LDPlayer Storage Optimizer
cd /d "%~dp0"

if exist "C:\Applio-3.6.4\env\python.exe" (
    start "" "C:\Applio-3.6.4\env\python.exe" ld_optimizer_app.py
    exit /b
)

where python >nul 2>nul
if %errorlevel% equ 0 (
    start "" python ld_optimizer_app.py
    exit /b
)

echo Python not found! Please ensure Python is installed or edit Run_LDOptimizer.bat.
pause
