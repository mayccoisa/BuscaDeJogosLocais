# Build and Deploy script for BuscaDeJogosLocais
#
# Builds the extension (using the Local csproj which references the local Playnite SDK)
# and copies it to the Playnite extensions folder.

$extensionId       = "186d9374-4173-420d-b17a-e2ace45bb317"
$playniteExtensions = "C:\Users\mayco\Downloads\Playnite\Extensions\$extensionId"
$msbuildPath       = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
$projectPath       = ".\BuscaDeJogosLocais.Local.csproj"
$outputPath        = ".\bin\LocalRelease"

Write-Host "Building project at $projectPath..." -ForegroundColor Cyan
& $msbuildPath $projectPath /t:Rebuild /p:Configuration=Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Deploying to $playniteExtensions..." -ForegroundColor Cyan
if (!(Test-Path $playniteExtensions)) {
    New-Item -ItemType Directory -Force -Path $playniteExtensions | Out-Null
}

Copy-Item "$outputPath\BuscaDeJogosLocais.dll" $playniteExtensions -Force
Copy-Item ".\extension.yaml" $playniteExtensions -Force
Copy-Item ".\icon.png" $playniteExtensions -Force

if (Test-Path ".\Localization") {
    Copy-Item ".\Localization" $playniteExtensions -Recurse -Force
}

Write-Host "Done! Please restart Playnite to see the changes." -ForegroundColor Green
