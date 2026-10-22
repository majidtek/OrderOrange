<#
.SYNOPSIS
  Builds and publishes the Windows till (LocalHandler): one self-contained exe that is both
  the installer partners download from Settings and the update every installed till pulls.

.DESCRIPTION
  The exe carries its own .NET runtime, so a shop PC needs nothing installed first. Run from
  the browser's download folder it installs itself (Start menu, desktop, Settings > Apps, no
  admin rights); run from the install folder it is the till. The update zip holds the same
  exe, so installed tills, older hand-unzipped tills and new installs all run one build.

  Writes into each till folder:
    OrderOrangeTill-Setup-<version>.exe   what "Download for Windows" serves (api/till/setup)
    LocalHandler-<version>.zip            what tills update from (api/till/download)
    version.json                          { version, file, sha256, notes, setup }

  The version comes from LocalHandler/LocalHandler.csproj (<Version>); bump it there first.

.EXAMPLE
  pwsh tools/publish-windows.ps1 -Notes "Installs itself: Start menu, desktop, Settings > Apps."
  pwsh tools/publish-windows.ps1 -Notes "..." -StagingOnly
#>
param(
    [Parameter(Mandatory)] [string] $Notes,
    [switch] $StagingOnly,
    [string] $Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'LocalHandler\LocalHandler.csproj'
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "LocalHandler.csproj <Version> must look like 1.2.3 (got '$version')" }

# One file, runtime included, native libraries (WebView2Loader, WPF) unpacked on first start.
$out = Join-Path $root "_publish\localhandler-$version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
& $Dotnet publish $proj -c Release -r win-x64 --self-contained true -o $out `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
$exe = Join-Path $out 'LocalHandler.exe'
if (-not (Test-Path $exe)) { throw "Publish produced no LocalHandler.exe in $out" }

$setupName = "OrderOrangeTill-Setup-$version.exe"
$zipName = "LocalHandler-$version.zip"
$stage = Join-Path $root "_publish\localhandler-$version-package"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
Copy-Item $exe (Join-Path $stage $setupName)
$zipPath = Join-Path $stage $zipName
Compress-Archive -Path $exe -DestinationPath $zipPath   # the updater looks for LocalHandler.exe at the zip's root

$hash = (Get-FileHash -Algorithm SHA256 $zipPath).Hash
$json = [ordered]@{ version = $version; file = $zipName; sha256 = $hash; notes = $Notes; setup = $setupName } | ConvertTo-Json

$targets = @('D:\OrderOrange\staging\publish-api\till')
if (-not $StagingOnly) { $targets += 'D:\OrderOrange\publish-api\till' }
foreach ($dir in $targets) {
    if (-not (Test-Path $dir)) { throw "Till folder missing: $dir" }
    foreach ($name in @($setupName, $zipName)) {
        Copy-Item (Join-Path $stage $name) (Join-Path $dir $name) -Force
        $a = (Get-FileHash -Algorithm SHA256 (Join-Path $stage $name)).Hash
        $b = (Get-FileHash -Algorithm SHA256 (Join-Path $dir $name)).Hash
        if ($a -ne $b) { throw "Copy of $name to $dir is corrupt (sha256 mismatch)" }
    }
    # version.json last: a till that reads it must find both files already in place.
    [IO.File]::WriteAllText((Join-Path $dir 'version.json'), $json + "`n", (New-Object Text.UTF8Encoding $false))
    $mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "published v$version ($mb MB) to $dir"
}

# The API serves the folder straight from disk; no restart. Confirm what the world sees.
$hosts = @('https://orderorange.com:8600')
if (-not $StagingOnly) { $hosts += 'https://api.orderorange.com' }
foreach ($h in $hosts) {
    try {
        $v = Invoke-RestMethod "$h/api/till/version" -TimeoutSec 20
        Write-Host "$h -> version $($v.version) file $($v.file) setup $($v.setup)"
    }
    catch { Write-Warning "$h did not answer: $($_.Exception.Message)" }
}
