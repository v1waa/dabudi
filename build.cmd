@echo off
setlocal
cd /d "%~dp0"

dotnet restore "Source\dabudi\dabudi.csproj" --locked-mode
if errorlevel 1 exit /b %errorlevel%

dotnet build "Source\dabudi\dabudi.csproj" -c Release --no-restore
exit /b %errorlevel%
