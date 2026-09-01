@echo off
setlocal
cd /d "%~dp0"
call build.cmd
if errorlevel 1 exit /b %errorlevel%
dotnet publish src\Dabudi.App\Dabudi.App.csproj -c Release --no-restore -o dist
exit /b %errorlevel%
