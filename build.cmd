@echo off
rem Baut Tandem.exe aus dem Quellcode. Der C#-Compiler gehoert zu Windows (.NET Framework 4.x).
rem Aufruf: build.cmd [version]. Ohne Version gilt das letzte Git-Tag (git describe), ohne Git 0.0.0-dev.
setlocal
pushd "%~dp0"
set "VERSION=%~1"
if not defined VERSION for /f "delims=" %%v in ('git describe --tags --match "v*" 2^>nul') do set "VERSION=%%v"
if not defined VERSION set "VERSION=0.0.0-dev"
if "%VERSION:~0,1%"=="v" set "VERSION=%VERSION:~1%"
rem Dieselbe Zeile schreibt der Updater (Updater.BuildInfo).
> src\BuildInfo.cs echo [assembly: System.Reflection.AssemblyInformationalVersion("%VERSION%")]
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /out:Tandem.exe @src\tandem.rsp
set "RESULT=%ERRORLEVEL%"
if "%RESULT%"=="0" echo Tandem.exe %VERSION% gebaut
popd
exit /b %RESULT%
