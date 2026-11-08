param([Parameter(Mandatory)][string]$AppDirectory,[Parameter(Mandatory)][string]$OutputFile,[int]$Seconds=30)
$ErrorActionPreference='Stop'
$appPath=(Resolve-Path -LiteralPath $AppDirectory).Path
$exe=Join-Path $appPath 'VPet-Simulator.Windows.exe'
if (-not (Test-Path -LiteralPath $exe) -or $Seconds -lt 5) { throw 'Invalid benchmark input.' }
$started=[DateTime]::UtcNow
$clock=[Diagnostics.Stopwatch]::StartNew()
$pet=Start-Process -FilePath $exe -ArgumentList 'diagnostic' -WorkingDirectory $appPath -WindowStyle Hidden -PassThru
try {
    $diagnostic=Join-Path $appPath 'rimuru-diagnostics.json'
    do {
        Start-Sleep -Milliseconds 100
        $pet.Refresh()
        if ($pet.HasExited) { throw 'Application exited during startup.' }
        if ($clock.Elapsed.TotalSeconds -gt 45) { throw 'Startup timed out.' }
    } until ((Test-Path -LiteralPath $diagnostic) -and (Get-Item -LiteralPath $diagnostic).LastWriteTimeUtc -ge $started)
    $readyMs=$clock.Elapsed.TotalMilliseconds
    $report=Get-Content -LiteralPath $diagnostic -Raw | ConvertFrom-Json
    if ($report.errors.Count -or @($report.graphs | Where-Object { $_.failed -or -not $_.ready }).Count) { throw 'Animation readiness failed.' }
    Start-Sleep -Seconds 30
    $pet.Refresh(); $cpuStart=$pet.TotalProcessorTime.TotalMilliseconds
    $sampleClock=[Diagnostics.Stopwatch]::StartNew();$working=@();$private=@()
    for ($i=0;$i -lt $Seconds;$i++) {
        Start-Sleep -Seconds 1
        $pet.Refresh()
        if ($pet.HasExited) { throw 'Application exited during sampling.' }
        $working+=$pet.WorkingSet64/1MB;$private+=$pet.PrivateMemorySize64/1MB
    }
    $elapsed=$sampleClock.Elapsed.TotalMilliseconds
    $result=[ordered]@{app=$appPath;sample_seconds=$elapsed/1000;startup_ready_ms=$readyMs;cpu_core_percent=100*($pet.TotalProcessorTime.TotalMilliseconds-$cpuStart)/$elapsed;mean_working_set_mib=($working|Measure-Object -Average).Average;mean_private_mib=($private|Measure-Object -Average).Average;ready_graphs=$report.graphCount;failed_graphs=0}
    $result | ConvertTo-Json | Set-Content -LiteralPath $OutputFile -Encoding utf8
    $result | ConvertTo-Json
} finally {
    $pet.Refresh()
    if (-not $pet.HasExited) {
        $null=$pet.CloseMainWindow()
        if (-not $pet.WaitForExit(5000)) { Stop-Process -Id $pet.Id -ErrorAction SilentlyContinue }
    }
}
