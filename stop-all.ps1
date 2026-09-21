# Stops every running OrderOrange service (any dotnet process serving a project from this folder).
Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
    Where-Object { $_.CommandLine -like '*OrderOrange*' } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Host "Stopped PID $($_.ProcessId)"
    }
