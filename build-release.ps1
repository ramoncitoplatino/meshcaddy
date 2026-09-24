param([string]$Version = "1.0.0")

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$DotNet = "C:\Program Files\dotnet\dotnet.exe"
$InnoCompiler = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not (Test-Path -LiteralPath $DotNet)) { throw ".NET 8 SDK was not found at $DotNet" }
if (-not $InnoCompiler) { throw "Inno Setup 6 was not found." }

& $DotNet publish "$ProjectRoot\MeshCaddy.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o "$ProjectRoot\artifacts\publish"
if ($LASTEXITCODE -ne 0) { throw "Publishing MeshCaddy failed." }

& $InnoCompiler "/DMyAppVersion=$Version" "$ProjectRoot\installer\MeshCaddy.iss"
if ($LASTEXITCODE -ne 0) { throw "Building the installer failed." }

Write-Host "Release created in $ProjectRoot\Releases"
