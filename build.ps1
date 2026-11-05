# Publishes MailBridge self-contained (no installed .NET runtime or Windows App SDK needed) to
# artifacts\<Runtime>\ and builds the installer artifacts\MailBridge-<Version>-<Platform>.msi from it
# (WiX Toolset 5, restored automatically via NuGet on the first build).
#
# Usage: .\build.ps1 [-Runtime win-x64|win-arm64] [-SkipMsi]
# Requires: .NET 10 SDK
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [switch]$SkipMsi
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root "artifacts\$Runtime"
$platform = $Runtime.Substring(4)
$version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args[0]) failed (exit code $LASTEXITCODE)." }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

Invoke-Dotnet publish "$root\src\MailBridge.App\MailBridge.App.csproj" -c $Configuration -r $Runtime "-p:Platform=$platform" --self-contained -o $out

# Debug symbols do not belong in the installation package.
Get-ChildItem $out -Recurse -Filter *.pdb | Remove-Item

Write-Host "Package: $out ($version)" -ForegroundColor Green

if (-not $SkipMsi) {
    $msiOut = Join-Path $root "installer\bin\$platform\$Configuration"
    Invoke-Dotnet build "$root\installer\MailBridge.Installer.wixproj" -c $Configuration "-p:Platform=$platform" "-p:Version=$version" "-p:PackageSource=$out\"
    $msi = Get-ChildItem $msiOut -Filter "MailBridge-$version-$platform.msi" | Select-Object -First 1
    Copy-Item $msi.FullName (Join-Path $root 'artifacts') -Force
    Write-Host "Installer: $(Join-Path $root "artifacts\$($msi.Name)")" -ForegroundColor Green
}
