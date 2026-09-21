# Starts the OrderOrange API if it is not already running. Idempotent on purpose: a
# scheduled task runs this every few minutes, so "already up" must be a no-op, not a
# second copy fighting for port 8520 (the TLS loopback the web apps call).
#
# The four IIS-hosted web apps are useless without it — when this process is missing,
# every sign-in and every menu simply fails.

$exe = "D:\OrderOrange\publish-api\OrderOrange.ApiServer.exe"

# Already listening? Then there is nothing to do. Checking the PORT rather than the
# process name also catches a process that is alive but wedged before binding.
$listening = Get-NetTCPConnection -LocalPort 8520 -State Listen -ErrorAction SilentlyContinue
if ($listening) { return }

# A process with no port is worse than none: it holds the database and the certificate
# but serves nothing, and a fresh copy could not bind. Clear it out first.
Get-Process -Name "OrderOrange.ApiServer" -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

if (Test-Path $exe) {
    # The published build: no SDK, no compile step, and immune to a stray build locking
    # the binaries out from under it — which is exactly how it died before.
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden
} else {
    # Fallback for a machine where the API has never been published.
    Start-Process -FilePath "C:\Program Files\dotnet\dotnet.exe" `
        -ArgumentList 'run --project D:\OrderOrange\OrderOrange.ApiServer' -WindowStyle Hidden
}
