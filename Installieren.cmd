@echo off
rem Baut Tandem und installiert es fuer diesen Laptop (ohne Adminrechte).
echo Welcher Laptop ist das?
echo   [A] Arbeitslaptop, Monitor an USB-C
echo   [P] Privatlaptop, Monitor an DisplayPort
choice /c AP /n /m "Auswahl (A/P): "
if errorlevel 2 (set "ROLLE=privat") else (set "ROLLE=arbeit")
call "%~dp0build.cmd" || goto fehler
"%~dp0Tandem.exe" install %ROLLE% || goto fehler
exit /b 0

:fehler
echo.
echo Installation fehlgeschlagen.
pause
exit /b 1
