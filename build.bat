@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo Building PowerToys Run Google Translate Plugin
echo ========================================================
echo.

where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] .NET SDK is not installed or not in your PATH.
    echo Please install .NET SDK 8.0 from: https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

echo [DEBUG] Current directory: %CD%
dotnet --version
echo.

set OUTPUT_DIR=bin\Release\Community.PowerToys.Run.Plugin.GoogleTranslate
set TARGET_DIR=%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\GoogleTranslate

echo [1/3] Compiling solution in Release configuration...
dotnet build -c Release -o "%OUTPUT_DIR%"

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Build failed! Check the compiler errors above.
    echo.
    pause
    exit /b %errorlevel%
)

echo.
echo [2/3] Copying plugin.json and Assets...
if not exist "%OUTPUT_DIR%\Images" mkdir "%OUTPUT_DIR%\Images"
if exist plugin.json copy /Y plugin.json "%OUTPUT_DIR%\" >nul
if exist Images copy /Y Images\* "%OUTPUT_DIR%\Images\" >nul 2>&1

echo.
echo [3/3] Installing to your plugins directory:
echo %TARGET_DIR%
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"
xcopy /E /Y /I "%OUTPUT_DIR%\*" "%TARGET_DIR%\" >nul

echo.
echo ========================================================
echo [SUCCESS] Plugin successfully built and installed!
echo ========================================================
echo.
echo Steps to activate:
echo 1. Restart PowerToys
echo 2. Type: tr <keyword>
echo.
pause
endlocal