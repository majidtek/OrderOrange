# Starts the whole OrderOrange platform (API + 4 web apps) in the background.
# URLs after start: API :8500, Customer :8501, Restaurant :8502, Driver :8503, Admin :8504
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$root = $PSScriptRoot

& $dotnet build "$root\OrderOrange.slnx" -v q --nologo

foreach ($project in 'OrderOrange.ApiServer', 'OrderOrange.ClientWeb', 'OrderOrange.RestaurantWeb', 'OrderOrange.DeliveryWeb', 'OrderOrange.AdminWeb')
{
    Start-Process -FilePath $dotnet -ArgumentList "run --project `"$root\$project`" --no-build" -WindowStyle Hidden
    Write-Host "Started $project"
}

Write-Host "`nAll services starting. Customer app: http://localhost:8501"
