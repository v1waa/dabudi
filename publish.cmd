@echo off
setlocal
cd /d "%~dp0"

dotnet restore "Source\dabudi\dabudi.csproj" --locked-mode
if errorlevel 1 exit /b %errorlevel%

dotnet publish "Source\dabudi\dabudi.csproj" -c Release --no-restore -o "%~dp0dist"
if errorlevel 1 exit /b %errorlevel%

echo.
echo Ready: "%~dp0dist\dabudi.exe"
exit /b 0
