@echo off
echo ==============================================
echo Building Google Translate for PowerToys Run
echo Target: Bengali (bn), Source: English (en)
echo ==============================================

where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] .NET 8 SDK is not installed or not in PATH.
    echo Please install .NET 8 SDK from https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo.
echo Publishing plugin files to .\Output\GoogleTranslate ...
echo.

if exist "Output" rd /s /q "Output"

dotnet publish -c Release -r win-x64 --self-contained false -o "Output\GoogleTranslate"
if not exist "Output\GoogleTranslate\Images" mkdir "Output\GoogleTranslate\Images"
xcopy /y /e "Images\*" "Output\GoogleTranslate\Images\" >nul 2>nul

if %errorlevel% equ 0 (
    rem Clean up intermediate bin directory to avoid duplicate folders
    if exist "bin" rd /s /q "bin"

    echo.
    echo ================================================================
    echo [SUCCESS] Everything is extracted and ready!
    echo.
    echo All files are placed in:
    echo   %cd%\Output\GoogleTranslate\
    echo.
    echo Next step:
    echo   1. Copy the "GoogleTranslate" folder from "Output"
    echo   2. Paste it into:
    echo      %%LOCALAPPDATA%%\Microsoft\PowerToys\PowerToys Run\Plugins\
    echo   3. Restart PowerToys
    echo ================================================================
    echo.
    explorer "Output"
) else (
    echo.
    echo [FAILED] Compilation encountered errors.
)
pause
