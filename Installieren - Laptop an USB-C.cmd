@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Own 54 -Other 0x0F
pause
