@echo off
title Gomoku Launcher
cd /d "%~dp0"

set "APP_EXE=src\Gomoku.App\bin\Debug\net8.0-windows\Gomoku.App.exe"

if exist "%APP_EXE%" goto run

echo First run: building the project, please wait...
dotnet build Gomoku.sln >nul
if errorlevel 1 (
    echo Build failed. Please make sure .NET 8 SDK is installed.
    pause
    exit /b 1
)

:run
start "" "%APP_EXE%"
exit /b 0
