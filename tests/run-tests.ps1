# Compila e executa os testes de regressao da logica central (LocalGameUtils).
# Usa o csc.exe do .NET Framework (mesma toolchain do plugin) - sem NuGet/SDK.
# Retorna exit code 0 se todos os testes passarem; 1 caso contrario.

$ErrorActionPreference = "Stop"

$root      = Split-Path -Parent $PSScriptRoot
$csc       = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$outDir    = Join-Path $PSScriptRoot "bin"
$outExe    = Join-Path $outDir "RegressionTests.exe"

$sources = @(
    (Join-Path $root "LocalGameUtils.cs"),
    (Join-Path $PSScriptRoot "RegressionTests.cs")
)

if (!(Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

Write-Host "Compilando testes de regressao..." -ForegroundColor Cyan
& $csc /nologo /optimize+ /out:"$outExe" $sources
if ($LASTEXITCODE -ne 0) {
    Write-Host "Falha ao compilar os testes." -ForegroundColor Red
    exit 1
}

Write-Host "Executando..." -ForegroundColor Cyan
Write-Host ""
& $outExe
$testExit = $LASTEXITCODE

Write-Host ""
if ($testExit -eq 0) {
    Write-Host "TODOS OS TESTES PASSARAM." -ForegroundColor Green
} else {
    Write-Host "EXISTEM TESTES FALHANDO." -ForegroundColor Red
}
exit $testExit
