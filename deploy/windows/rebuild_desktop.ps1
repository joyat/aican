# Rebuild AiCan Desktop v5.1.1 and relaunch.

$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$proj = "$root\src\AiCan.Desktop\AiCan.Desktop.csproj"
$exe  = "$root\src\AiCan.Desktop\bin\Release\net8.0-windows10.0.19041.0\AiCan.Desktop.exe"
$task = "AiCanDesktopLaunch"
$currentUser = $env:USERNAME

Write-Host "=== AiCan Desktop v5.1.1 rebuild ===" -ForegroundColor Cyan

# Kill running instance
Get-Process -Name "AiCan.Desktop" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

# Build
Write-Host "Building..."
dotnet build $proj -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }

# Launch in interactive session via scheduled task
Write-Host "Launching in interactive session..."
schtasks /create /f /tn $task /sc once /st 00:00 /tr $exe /ru $currentUser /it | Out-Null
schtasks /run /tn $task | Out-Null

Write-Host "=== Done - AiCan Desktop v5.1.1 launched ===" -ForegroundColor Green
