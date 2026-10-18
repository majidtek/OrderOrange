<#
.SYNOPSIS
  Publishes a new Android till APK: copies it into the prod and staging till folders and
  rewrites android.json (file, version, sha256, notes) so partner Settings offers the download
  and installed tills see the update (api/till/android/version|download).

.EXAMPLE
  pwsh tools/publish-android.ps1 -Apk C:\Users\Administrator\Downloads\com.orderorange.till-Signed.apk -Version 1.1.0 -Notes "Bluetooth printers (InnerPrinter), Find printers lists paired devices."
  pwsh tools/publish-android.ps1 -Apk ... -Version 1.1.0 -Notes "..." -StagingOnly
#>
param(
    [Parameter(Mandatory)] [string] $Apk,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Notes,
    [switch] $StagingOnly
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Apk)) { throw "APK not found: $Apk" }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }

$name = "OrderOrangeTill-$Version.apk"
$hash = (Get-FileHash -Algorithm SHA256 $Apk).Hash
$json = [ordered]@{ file = $name; version = $Version; sha256 = $hash; notes = $Notes } | ConvertTo-Json
$targets = @('D:\OrderOrange\staging\publish-api\till')
if (-not $StagingOnly) { $targets += 'D:\OrderOrange\publish-api\till' }

foreach ($dir in $targets) {
    if (-not (Test-Path $dir)) { throw "Till folder missing: $dir" }
    Copy-Item $Apk (Join-Path $dir $name) -Force
    $check = (Get-FileHash -Algorithm SHA256 (Join-Path $dir $name)).Hash
    if ($check -ne $hash) { throw "Copy to $dir is corrupt (sha256 mismatch)" }
    [IO.File]::WriteAllText((Join-Path $dir 'android.json'), $json + "`n", (New-Object Text.UTF8Encoding $false))
    Write-Host "published $name ($([math]::Round((Get-Item $Apk).Length / 1MB, 1)) MB) to $dir"
}

# The API serves the folder straight from disk — no restart needed. Confirm what the world sees.
$hosts = @('https://orderorange.com:8600')
if (-not $StagingOnly) { $hosts += 'https://api.orderorange.com' }
foreach ($h in $hosts) {
    try {
        $v = Invoke-RestMethod "$h/api/till/android/version" -TimeoutSec 20
        Write-Host "$h -> version $($v.version) file $($v.file)"
    }
    catch { Write-Warning "$h did not answer: $($_.Exception.Message)" }
}
