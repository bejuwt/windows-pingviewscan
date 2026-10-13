param(
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$selfContained = -not $FrameworkDependent
Write-Host "Publishing PingViewScan ($Runtime, self-contained=$selfContained)..."

dotnet publish src/PingViewScan.App/PingViewScan.App.csproj `
    -c Release `
    -r $Runtime `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

$out = "src/PingViewScan.App/bin/Release/net10.0-windows/$Runtime/publish"
Write-Host ""
Write-Host "Done. Executable in: $out"
