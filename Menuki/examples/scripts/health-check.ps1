# Demo health check script for Menuki plugin-demo example
param(
    [switch]$Verbose
)

Write-Host "=============================="
Write-Host "  System Health Check"
Write-Host "=============================="
Write-Host ""

Write-Host "[1/4] CPU..."
if ($Verbose) { Write-Host "  Checking processor load..." }
Start-Sleep -Seconds 1
Write-Host "  OK - CPU usage normal"
Write-Host ""

Write-Host "[2/4] Memory..."
if ($Verbose) { Write-Host "  Checking memory allocation..." }
Start-Sleep -Seconds 1
Write-Host "  OK - Memory within limits"
Write-Host ""

Write-Host "[3/4] Disk..."
if ($Verbose) { Write-Host "  Checking disk space..." }
Start-Sleep -Seconds 1
Write-Host "  OK - Sufficient disk space"
Write-Host ""

Write-Host "[4/4] Network..."
if ($Verbose) { Write-Host "  Checking connectivity..." }
Start-Sleep -Seconds 1
Write-Host "  OK - Network reachable"
Write-Host ""

Write-Host "=============================="
Write-Host "  All checks passed!"
Write-Host "=============================="
