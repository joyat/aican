# AiCan full deploy — run from Windows PowerShell
# Pulls updated files from Mac, pushes Services.cs to Ubuntu, rebuilds both
$ErrorActionPreference = "Continue"
$ubuntu   = "joyat@sungas-ubuntulab.tail6932f9.ts.net"
$mac      = "joyatsaha@joys-macbookpro.tail6932f9.ts.net"
$winRoot  = "C:\Users\joyat\projects\aican"
$ubuntuRoot = "/home/joyat/projects/aican"
$macRoot  = "/Users/joyatsaha/projects/aican"

Write-Host "=== 1/4  Pull updated source from Mac ===" -ForegroundColor Cyan
scp "${mac}:${macRoot}/src/AiCan.Api/Services.cs"           "$winRoot\src\AiCan.Api\Services.cs"
scp "${mac}:${macRoot}/src/AiCan.Desktop/App.xaml"          "$winRoot\src\AiCan.Desktop\App.xaml"
scp "${mac}:${macRoot}/src/AiCan.Desktop/MainWindow.xaml"   "$winRoot\src\AiCan.Desktop\MainWindow.xaml"

Write-Host "=== 2/4  Push Services.cs to Ubuntu + rebuild API ===" -ForegroundColor Cyan
scp "$winRoot\src\AiCan.Api\Services.cs" "${ubuntu}:${ubuntuRoot}/src/AiCan.Api/Services.cs"
ssh $ubuntu @"
cd $ubuntuRoot
fuser -k 5000/tcp 2>/dev/null || true
sleep 3
dotnet build src/AiCan.Api/AiCan.Api.csproj -c Release --nologo -v q
mkdir -p .runtime/logs
nohup dotnet src/AiCan.Api/bin/Release/net8.0/AiCan.Api.dll --urls http://0.0.0.0:5000 > .runtime/logs/api.log 2>&1 &
"@
Start-Sleep -Seconds 5
Write-Host "Ubuntu healthz: " -NoNewline
curl -s http://sungas-ubuntulab.tail6932f9.ts.net:5000/healthz

Write-Host ""
Write-Host "=== 3/4  Rebuild Windows desktop ===" -ForegroundColor Cyan
Stop-Process -Name "AiCan.Desktop" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
dotnet build "$winRoot\src\AiCan.Desktop\AiCan.Desktop.csproj" -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }

Write-Host "=== 4/4  Relaunch AiCan Desktop ===" -ForegroundColor Cyan
$exe = "$winRoot\src\AiCan.Desktop\bin\Release\net8.0-windows10.0.19041.0\AiCan.Desktop.exe"
schtasks /create /f /tn AiCanDeploy /sc once /st 00:00 /tr $exe /ru joyat /it | Out-Null
schtasks /run /tn AiCanDeploy | Out-Null
Write-Host "=== Done ===" -ForegroundColor Green
