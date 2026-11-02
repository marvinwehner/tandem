@echo off
rem Zeichnet das App-Icon neu (tools\IconGenerator.cs) und schreibt src\tandem.ico. Danach build.cmd.
setlocal
pushd "%~dp0.."
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /codepage:65001 /r:System.Drawing.dll /out:tools\IconGenerator.exe tools\IconGenerator.cs
if errorlevel 1 (
    popd
    exit /b 1
)
tools\IconGenerator.exe src\tandem.ico
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
