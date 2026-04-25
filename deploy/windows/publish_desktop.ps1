# Publish a versioned self-contained AiCan Desktop package for distribution.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$proj = Join-Path $root "src\AiCan.Desktop\AiCan.Desktop.csproj"
$version = "v5.2"
$rid = "win-x64"
$artifactName = "AiCan-Desktop-$version-$rid"
$publishDir = Join-Path $root "artifacts\desktop\$artifactName"
$zipPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "$artifactName.zip"

Write-Host "=== AiCan Desktop publish $version ===" -ForegroundColor Cyan

if (Test-Path $publishDir) {
  Remove-Item $publishDir -Recurse -Force
}

dotnet publish $proj `
  -c Release `
  -r $rid `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishReadyToRun=true `
  -o $publishDir

if ($LASTEXITCODE -ne 0) {
  Write-Host "PUBLISH FAILED" -ForegroundColor Red
  exit 1
}

if (Test-Path $zipPath) {
  Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host "Publish folder: $publishDir" -ForegroundColor Green
Write-Host "Zip package:    $zipPath" -ForegroundColor Green
