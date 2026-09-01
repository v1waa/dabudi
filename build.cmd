@echo off
setlocal
cd /d "%~dp0"
dotnet restore dabudi.sln --locked-mode
if errorlevel 1 exit /b %errorlevel%
dotnet build dabudi.sln -c Release --no-restore
if errorlevel 1 exit /b %errorlevel%
dotnet tests\Dabudi.Tests\bin\Release\net8.0\Dabudi.Tests.dll
exit /b %errorlevel%
