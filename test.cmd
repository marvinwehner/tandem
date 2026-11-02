@echo off
rem Baut und startet die Tests (tests\Tests.cs, ohne Test-Framework).
setlocal
pushd "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /main:Tests /out:tests\Tests.exe @src\tandem.rsp tests\Tests.cs
if errorlevel 1 (
    popd
    exit /b 1
)
tests\Tests.exe
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
