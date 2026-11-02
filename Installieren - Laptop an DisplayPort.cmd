@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Own 0x0F -Other 54
pause
