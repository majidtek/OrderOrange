<#
.SYNOPSIS
  Publishes a new OrderOrange Partner app (OrderOrange.PartnerApp) build: copies the Android APK
  and/or the Windows zip into the prod and staging till folders and rewrites the platform's
  manifest (partner-android.json / partner-windows.json: file, version, sha256, notes), so partner
  Settings offers the download (api/till/partner/{android|windows}/version|download).

.EXAMPLE
  pwsh tools/publish-partner-app.ps1 -Platform android -File C:\Windows\Temp\com.orderorange.partner-Signed.apk -Version 1.0.0 -Notes "First release"
  pwsh tools/publish-partner-app.ps1 -Platform windows -File C:\Windows\Temp\OrderOrangePartner-win-x64.zip -Version 1.0.0 -Notes "First release" -StagingOnly
#>
param(
    [Parameter(Mandatory)] [ValidateSet('android', 'windows')] [string] $Platform,
    [Parameter(Mandatory)] [string] $File,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Notes,
    [switch] $StagingOnly
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $File)) { throw "File not found: $File" }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }

$ext = if ($Platform -eq 'android') { 'apk' } else { 'zip' }
$name = "OrderOrangePartner-$Platform-$Version.$ext"
$manifest = "partner-$Platform.json"
$hash = (Get-FileHash -Algorithm SHA256 $File).Hash
$json = [ordered]@{ file = $name; version = $Version; sha256 = $hash; notes = $Notes } | ConvertTo-Json
$targets = @('D:\OrderOrange\staging\publish-api\till')
if (-not $StagingOnly) { $targets += 'D:\OrderOrange\publish-api\till' }

foreach ($dir in $targets) {
    if (-not (Test-Path $dir)) { throw "Till folder missing: $dir" }
    Copy-Item $File (Join-Path $dir $name) -Force
    $check = (Get-FileHash -Algorithm SHA256 (Join-Path $dir $name)).Hash
    if ($check -ne $hash) { throw "Copy to $dir is corrupt (sha256 mismatch)" }
    [IO.File]::WriteAllText((Join-Path $dir $manifest), $json + "`n", (New-Object Text.UTF8Encoding $false))
    Write-Host "published $name ($([math]::Round((Get-Item $File).Length / 1MB, 1)) MB) to $dir"
}

# The API serves the folder straight from disk; no restart needed. Confirm what the world sees.
$hosts = @('https://orderorange.com:8600')
if (-not $StagingOnly) { $hosts += 'https://api.orderorange.com' }
foreach ($h in $hosts) {
    try {
        $v = Invoke-RestMethod "$h/api/till/partner/$Platform/version" -TimeoutSec 20
        Write-Host "$h -> $Platform version $($v.version) file $($v.file)"
    }
    catch { Write-Warning "$h did not answer: $($_.Exception.Message)" }
}
