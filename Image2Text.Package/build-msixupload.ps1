# Builds an .msixupload for Microsoft Store submission of Image2Text OCR.
# Run from PowerShell on Windows. Requires the Windows 10/11 SDK (makeappx).
#
# Steps:
#   1. dotnet publish the WPF app (Release, win-x64, self-contained: false).
#   2. Stage publish output + assets + manifest into a layout directory.
#   3. Pack into .msix with makeappx.
#   4. Wrap into .msixbundle.
#   5. Wrap into .msixupload (the Store-facing zip).
#
# Final artifact path is printed at the end.

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version       = "1.0.10.0"
)

$ErrorActionPreference = "Stop"

$Repo       = Split-Path -Parent $PSScriptRoot
$PkgDir     = $PSScriptRoot
$Csproj     = Join-Path $Repo "Image2Text.csproj"
$ManifestSrc= Join-Path $PkgDir "Package.appxmanifest"
$ImagesSrc  = Join-Path $PkgDir "Images"

$BuildRoot  = Join-Path $PkgDir "build"
$Layout     = Join-Path $BuildRoot "layout"
$OutDir     = Join-Path $PkgDir "AppPackages"
New-Item -ItemType Directory -Force -Path $BuildRoot,$OutDir | Out-Null
if (Test-Path $Layout) { Remove-Item $Layout -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Layout | Out-Null

# Locate makeappx in the latest installed Windows SDK.
$SdkBins = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Directory `
    | Where-Object { $_.Name -match '^10\.0\.' } `
    | Sort-Object Name -Descending
if (-not $SdkBins) { throw "Windows SDK not found under 'C:\Program Files (x86)\Windows Kits\10\bin'." }
$MakeAppx = Join-Path $SdkBins[0].FullName "x64\makeappx.exe"
if (-not (Test-Path $MakeAppx)) { throw "makeappx.exe not found at $MakeAppx" }
Write-Host "Using SDK: $($SdkBins[0].Name)" -ForegroundColor Cyan

# 1. dotnet publish (framework-dependent; the MSIX target machine has .NET 8 desktop runtime).
$PublishDir = Join-Path $BuildRoot "publish"
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
Write-Host "Publishing WPF app..." -ForegroundColor Cyan
& dotnet publish $Csproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $PublishDir `
    /p:PublishSingleFile=false `
    /p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# 2. Stage.
Write-Host "Staging package layout..." -ForegroundColor Cyan
Copy-Item "$PublishDir\*" -Destination $Layout -Recurse -Force
Copy-Item $ManifestSrc -Destination (Join-Path $Layout "AppxManifest.xml") -Force
Copy-Item $ImagesSrc -Destination $Layout -Recurse -Force
# Strip files makeappx rejects in MSIX root.
Get-ChildItem $Layout -Filter "*.pdb" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

# Inject the runtime version into the staged manifest's Identity element only.
# The negative lookbehind keeps MinVersion / MaxVersionTested untouched -
# otherwise the bundle's TargetDeviceFamily MinVersion gets clobbered with
# the package version (e.g. "1.0.10.0") and the Store rejects the upload.
$mf = Join-Path $Layout "AppxManifest.xml"
(Get-Content $mf -Raw) -replace '(?<![A-Za-z])Version="\d+\.\d+\.\d+\.\d+"', "Version=`"$Version`"" | Set-Content $mf -Encoding UTF8

# 3. Pack the inner package. Use .appx extension because makeappx bundle
#    rejects .msix payloads when the package targets pre-1903 device families.
$InnerPath = Join-Path $BuildRoot "Image2TextPackage_${Version}_x64.appx"
if (Test-Path $InnerPath) { Remove-Item $InnerPath -Force }
Write-Host "Packing inner .appx..." -ForegroundColor Cyan
& $MakeAppx pack /d $Layout /p $InnerPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed." }

# 4. Wrap into .msixbundle.
$BundleDir  = Join-Path $BuildRoot "bundle-input"
if (Test-Path $BundleDir) { Remove-Item $BundleDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $BundleDir | Out-Null
Copy-Item $InnerPath -Destination $BundleDir -Force
$BundlePath = Join-Path $BuildRoot "Image2TextPackage_${Version}_x64_bundle.msixbundle"
if (Test-Path $BundlePath) { Remove-Item $BundlePath -Force }
Write-Host "Packing .msixbundle..." -ForegroundColor Cyan
& $MakeAppx bundle /d $BundleDir /p $BundlePath /bv $Version /o
if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed." }

# 5. Wrap into .msixupload (a plain zip containing the bundle).
$UploadPath = Join-Path $OutDir "Image2TextPackage_${Version}_x64_bundle.msixupload"
if (Test-Path $UploadPath) { Remove-Item $UploadPath -Force }
$TmpZip = Join-Path $OutDir "Image2TextPackage_${Version}_x64_bundle.zip"
if (Test-Path $TmpZip) { Remove-Item $TmpZip -Force }
Write-Host "Wrapping into .msixupload..." -ForegroundColor Cyan
Compress-Archive -Path $BundlePath -DestinationPath $TmpZip -Force
Move-Item $TmpZip $UploadPath -Force

Write-Host ""
Write-Host "========================================================================" -ForegroundColor Green
Write-Host " .msixupload built." -ForegroundColor Green
Write-Host "========================================================================" -ForegroundColor Green
Write-Host " File path: $UploadPath" -ForegroundColor Green
Write-Host " Directory: $OutDir"   -ForegroundColor Green
Write-Host ""
Write-Host " Before uploading to Partner Center, the Identity Name + Publisher in"  -ForegroundColor Yellow
Write-Host " Package.appxmanifest must match your reservation. If they still say"   -ForegroundColor Yellow
Write-Host " REPLACE_WITH_..., the Store will reject the upload. Microsoft re-signs" -ForegroundColor Yellow
Write-Host " on the server, so no signtool call is needed for the Store upload."     -ForegroundColor Yellow
