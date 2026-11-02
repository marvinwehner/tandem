@echo off
rem Baut MonitorSwitch.exe aus dem Quellcode. Der C#-Compiler gehoert zu Windows (.NET Framework 4.x).
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0MonitorSwitch.exe" "%~dp0MonitorSwitch.cs" "%~dp0LogiSwitch.cs" "%~dp0Overlay.cs"
