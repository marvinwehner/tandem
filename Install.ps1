# Installiert MonitorSwitch mit Hotkey Strg+Alt+K: Monitor, Maus und Tastatur wechseln zum anderen Laptop.
# -Own ist der Monitoreingang dieses Laptops, -Other der des anderen (DisplayPort = 0x0F, USB-C = 54).
# Strg+Alt+M war auf dem RTX-Laptop schon von einem anderen Programm belegt. Strg+Alt+<Taste> ist auf deutschen
# Tastaturen gleich AltGr+<Taste>, deshalb keine Taste mit AltGr-Zeichen nehmen (Q=@, E=€, M=µ, Ziffern usw.).
param([string]$Own = "0x0F", [string]$Other = "54")
$dest = Join-Path $env:LOCALAPPDATA "Programs\MonitorSwitch"
$exe = Join-Path $dest "MonitorSwitch.exe"

& (Join-Path $PSScriptRoot "build.cmd")
if ($LASTEXITCODE -ne 0) { Write-Host "Build fehlgeschlagen"; exit 1 }

# Laufenden Hintergrundprozess beenden, sonst ist die exe gesperrt.
Get-Process MonitorSwitch -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $PSScriptRoot "MonitorSwitch.exe") $dest -Force

$shell = New-Object -ComObject WScript.Shell
# Startmenü-Eintrag zum Umschalten per Klick. Bewusst ohne Tastenkürzel: Die startet der Explorer erst nach ~3 s.
$lnkPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Monitor umschalten.lnk"
$lnk = $shell.CreateShortcut($lnkPath)
$lnk.TargetPath = $exe
$lnk.Arguments = "toggle $Own $Other"
$lnk.WorkingDirectory = $dest
$lnk.Hotkey = ""
$lnk.Description = "Monitor, Maus und Tastatur zum anderen Laptop umschalten"
$lnk.Save()

# Autostart: lauscht im Hintergrund auf Strg+Alt+K und reagiert sofort.
$autoPath = Join-Path ([Environment]::GetFolderPath("Startup")) "MonitorSwitch.lnk"
$auto = $shell.CreateShortcut($autoPath)
$auto.TargetPath = $exe
$auto.Arguments = "listen $Own $Other"
$auto.WorkingDirectory = $dest
$auto.Description = "MonitorSwitch: Strg+Alt+K schaltet zum anderen Laptop"
$auto.Save()
Start-Process $exe -ArgumentList "listen", $Own, $Other

Write-Host "Installiert: $exe"
Write-Host "Hotkey: Strg+Alt+K (Hintergrundprozess, Autostart: $autoPath)"
Write-Host "Argumente: $Own (dieser Laptop) / $Other (anderer Laptop)"
