@echo off
setlocal
cd /d "%~dp0"

echo Building CallAnalog Softphone...
dotnet publish CallAnalog.Softphone.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\publish
if errorlevel 1 exit /b 1

echo.
echo Build complete:
echo   %CD%\dist\publish\CallAnalog.Softphone.exe
echo.
pause
