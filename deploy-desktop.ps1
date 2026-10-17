# Build self-contained single-file exe with icon and copy it to the Desktop
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$log = Join-Path $root 'deploy-result.txt'
$lines = @()
Set-Location $root
$lines += '== publish =='
$out = dotnet publish cava.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true 2>&1
$lines += ($out | Select-Object -Last 5)
$exe = Join-Path $root 'bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\cava.exe'
if (Test-Path $exe) {
  $desktop = [Environment]::GetFolderPath('Desktop')
  $dest = Join-Path $desktop 'cava.exe'
  Copy-Item $exe $dest -Force
  $lines += ('OK copied ' + (Get-Item $dest).Length + ' bytes -> ' + $dest)
} else {
  $lines += 'FAIL publish output missing: ' + $exe
}
$lines | Set-Content $log