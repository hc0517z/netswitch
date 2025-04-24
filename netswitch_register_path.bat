@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion
:: Check for admin privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo This script requires administrator privileges.
    echo Please run it again as administrator.
    pause
    exit /b 1
)
:: Get current directory path
set "CURRENT_DIR=%~dp0"
set "CURRENT_DIR=%CURRENT_DIR:~0,-1%"
:: Get current PATH environment variable
for /f "tokens=2*" %%A in ('reg query "HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v PATH') do set "SYSTEM_PATH=%%B"
:: Check if already exists in PATH
echo !SYSTEM_PATH! | find /i "%CURRENT_DIR%" > nul
if not errorLevel 1 (
    echo "%CURRENT_DIR%" already exists in PATH.
    pause
    exit /b 0
)
:: Add current directory to PATH
set "NEW_PATH=%SYSTEM_PATH%;%CURRENT_DIR%"
:: Set the new PATH to system environment variable
reg add "HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v PATH /t REG_EXPAND_SZ /d "%NEW_PATH%" /f
if %errorLevel% neq 0 (
    echo Error occurred while updating PATH environment variable.
    pause
    exit /b 1
)
:: Broadcast environment variable change notification
powershell -command "& {$env:Path = [System.Environment]::GetEnvironmentVariable('Path','Machine'); $env:Path = [System.Environment]::GetEnvironmentVariable('Path','User') + ';' + $env:Path}"
echo "%CURRENT_DIR%" has been successfully added to PATH environment variable.
echo Restart command prompt to apply changes.
pause