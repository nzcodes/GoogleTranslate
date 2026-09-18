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
echo Publishing plugin files to .\GoogleTranslate ...
echo.

if exist "GoogleTranslate" rd /s /q "GoogleTranslate"

dotnet publish -c Release -r win-x64 --self-contained false -o "GoogleTranslate"
if not exist "GoogleTranslate\Images" mkdir "GoogleTranslate\Images"
xcopy /y /e "Images\*" "GoogleTranslate\Images\" >nul 2>nul

if %errorlevel% equ 0 (
    rem Clean up intermediate bin directory to avoid duplicate folders
    if exist "bin" rd /s /q "bin"

    echo.
    echo ================================================================
    echo [SUCCESS] Everything is extracted and ready!
    echo.
    echo All files are placed directly in:
    echo   %cd%\GoogleTranslate\
    echo.
    echo Next step:
    echo   1. Copy the "GoogleTranslate" folder directly from root
    echo   2. Paste it into:
    echo      %%LOCALAPPDATA%%\Microsoft\PowerToys\PowerToys Run\Plugins\
    echo   3. Restart PowerToys
    echo ================================================================
    echo.
) else (
    echo.
    echo [FAILED] Compilation encountered errors.
)
pause
