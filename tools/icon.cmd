@echo off
rem Zeichnet App-Icon und Tray-Varianten neu (tools\IconGenerator.cs) und schreibt sie nach src. Danach build.cmd.
setlocal
pushd "%~dp0.."
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /codepage:65001 /r:System.Drawing.dll /out:tools\IconGenerator.exe tools\IconGenerator.cs
if errorlevel 1 (
    popd
    exit /b 1
)
tools\IconGenerator.exe src
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
