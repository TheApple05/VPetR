param([string]$AppDirectory=(Join-Path $PSScriptRoot '..\dist\Rimuru-VPet'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\Update-Rimuru.ps1') -FunctionsOnly -AppDirectory $AppDirectory
$app=(Resolve-Path -LiteralPath $AppDirectory).Path
$dist=(Resolve-Path (Join-Path $PSScriptRoot '..\dist')).Path
if (-not $app.StartsWith($dist+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Use an isolated build under Rimuru/dist.' }
function Assert($Condition,$Message) { if (-not $Condition) { throw $Message } }
$started=[DateTime]::UtcNow
$pet=Start-Process -FilePath (Join-Path $app 'VPet-Simulator.Windows.exe') -WorkingDirectory $app -ArgumentList diagnostic -PassThru
$request=Join-Path $app "rimuru-update-$($pet.Id).request.json"
$ack=Join-Path $app "rimuru-update-$($pet.Id).ack.json"
function Send-Request($Version,$HelperPid) {
    @{Token=[guid]::NewGuid().ToString('N');Version=$Version;HelperPid=$HelperPid} | ConvertTo-Json | Set-Content -LiteralPath ($request+'.tmp') -Encoding utf8
    Move-Item -LiteralPath ($request+'.tmp') -Destination $request -Force
}
try {
    $clock=[Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 200
        Assert (-not $pet.HasExited -and $clock.Elapsed.TotalSeconds -lt 45) 'App startup failed.'
        $ready=Join-Path $app 'rimuru-diagnostics.json'
    } until ((Test-Path -LiteralPath $ready) -and (Get-Item -LiteralPath $ready).LastWriteTimeUtc -gt $started)
    Send-Request '0.0.1' $PID
    Start-Sleep -Seconds 3
    Assert (-not $pet.HasExited -and -not (Test-Path -LiteralPath $ack)) 'Downgrade request should be ignored.'
    Send-Request '9.0.0' -1
    Start-Sleep -Seconds 3
    Assert (-not $pet.HasExited -and -not (Test-Path -LiteralPath $ack)) 'Dead helper request should be ignored.'
    $settings=Get-Content (Join-Path $app 'Setting.lps') -Raw
    $counter=if ($settings -match 'savetimes#(\d+)') { [int]$Matches[1] } else { 100000 }
    $saves=Join-Path $app 'Saves';New-Item -ItemType Directory -Path $saves -Force | Out-Null
    $blocked=Join-Path $saves ('Save_'+($counter+1)+'.lps')
    $lock=[IO.File]::Open($blocked,'Create','ReadWrite','None')
    try {
        Send-Request '9.0.0' $PID
        $clock.Restart()
        do { Start-Sleep -Milliseconds 200; Assert ($clock.Elapsed.TotalSeconds -lt 15) 'Save-failure reply timed out.' } until (Test-Path -LiteralPath $ack)
        $reply=Get-Content -LiteralPath $ack -Raw | ConvertFrom-Json
        Assert (-not $reply.Saved -and -not $pet.HasExited) 'Failed save must leave the application running.'
    } finally { $lock.Dispose() }
    Remove-Item -LiteralPath $blocked
    $plans=[Collections.Generic.List[object]]::new();$requests=[Collections.Generic.List[string]]::new()
    Request-RimuruShutdown $app '9.0.0' $plans $requests
    Assert ($plans.Count -eq 1 -and $pet.HasExited) 'Saved application must exit without a force kill.'
    $saved=Get-Content -LiteralPath $ack -Raw | ConvertFrom-Json
    foreach ($file in $saved.Saves) { Assert ((Get-Item -LiteralPath (Join-Path $app $file)).Length -gt 0) 'Acknowledged snapshot missing.' }
    $settings=Get-Content (Join-Path $app 'Setting.lps') -Raw
    Assert ($settings -match 'savetimes#(\d+)') 'Updated save sequence is missing.'
    Assert ($saved.Saves[0] -eq ('Saves/Save_'+$Matches[1]+'.lps')) 'Settings and saved snapshot do not agree.'
    Restart-RimuruPlans $app $plans
    Start-Sleep -Seconds 7
    $reopened=@(Get-RimuruProcesses $app)
    Assert ($reopened.Count -eq 1 -and $reopened[0].Id -ne $pet.Id) 'Application should restart exactly once.'
    $plans2=[Collections.Generic.List[object]]::new();$requests2=[Collections.Generic.List[string]]::new()
    Request-RimuruShutdown $app '9.0.0' $plans2 $requests2
    Write-Output 'Passed: downgrade/dead-helper rejection, failed-save cancellation, verified fresh save, graceful exit, argument-preserving single restart.'
} finally {
    foreach ($instance in @(Get-RimuruProcesses $app)) { $null=$instance.CloseMainWindow();$null=$instance.WaitForExit(10000) }
}
