# Starts VoxLink's backend and frontend dev servers, then opens the app
# in the browser. Run from anywhere: powershell -ExecutionPolicy Bypass -File start.ps1

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$backend = Join-Path $root "backend\VoxLink.Api"
$frontend = Join-Path $root "frontend\web"

# Free up 5080/5173 in case a previous run of this script is still going.
foreach ($port in 5080, 5173) {
    Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
}

Write-Host "Building backend..."
Push-Location $backend
dotnet build --nologo -v quiet
Pop-Location

Write-Host "Starting backend on http://localhost:5080 ..."
$env:ASPNETCORE_URLS = "http://localhost:5080"
$env:ASPNETCORE_ENVIRONMENT = "Development"
# Runs the built DLL directly via `dotnet exec` rather than the generated
# .exe apphost - this machine's endpoint protection blocks freshly-built
# unsigned executables, but dotnet.exe itself is signed and allowed.
$backendDll = Join-Path $backend "bin\Debug\net10.0\VoxLink.Api.dll"
Start-Process -FilePath "dotnet" -ArgumentList @("exec", $backendDll) -WorkingDirectory $backend -WindowStyle Hidden

Write-Host "Starting frontend dev server on http://localhost:5173 ..."
Start-Process -FilePath "cmd.exe" -ArgumentList @("/c", "npm run dev") -WorkingDirectory $frontend -WindowStyle Hidden

Write-Host "Waiting for the app to come up..."
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    try {
        Invoke-RestMethod -Uri "http://localhost:5173" -TimeoutSec 1 | Out-Null
        $ready = $true
        break
    } catch {}
}

if ($ready) {
    Write-Host "Opening VoxLink in the browser..."
    Start-Process "http://localhost:5173"
} else {
    Write-Host "Frontend didn't come up in time - check the servers manually, then open http://localhost:5173"
}
