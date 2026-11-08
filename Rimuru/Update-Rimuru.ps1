param([string]$AppDirectory=$PSScriptRoot,[switch]$FunctionsOnly)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'

function Get-RimuruFileHash([string]$Path) {
    $stream=[IO.File]::OpenRead([IO.Path]::GetFullPath($Path))
    $algorithm=[Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','') }
    finally { $stream.Dispose();$algorithm.Dispose() }
}

function Expand-RimuruPackage([string]$Package,[string]$ExpectedVersion) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stage=Join-Path ([IO.Path]::GetTempPath()) ('Rimuru-install-'+[guid]::NewGuid())
    $payload=Join-Path $stage 'payload'
    $zip=[IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $total=0L
        if ($zip.Entries.Count -gt 10000) { throw 'Too many update files.' }
        foreach ($entry in $zip.Entries) {
            $name=$entry.FullName.Replace('\','/')
            if (-not $name.StartsWith('Rimuru-VPet/')) { throw 'Invalid update root.' }
            $relative=$name.Substring(12)
            if ($relative -match '(^|/)\.\.(/|$)|:|^/' -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe update path.' }
            $destination=[IO.Path]::GetFullPath((Join-Path $payload $relative))
            if (-not $destination.StartsWith($payload+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Update path escapes staging.' }
            $total+=$entry.Length
            if ($total -gt 2GB) { throw 'Update is too large.' }
            if ($name.EndsWith('/')) { continue }
            New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$false)
        }
    } finally { $zip.Dispose() }
    foreach ($required in 'VPet-Simulator.Windows.exe','mod/0000_core/info.lps','mod/0100_rimuru/info.lps','rimuru-version.txt') {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) { throw "Incomplete update: $required" }
    }
    if ((Get-Content (Join-Path $payload 'rimuru-version.txt') -Raw).Trim() -ne $ExpectedVersion) { throw 'Update version mismatch.' }
    return $payload
}

function Install-RimuruPayload([string]$payload,[string]$Target) {
    $targetRoot=[IO.Path]::GetFullPath($Target).TrimEnd('\')+'\'
    $backup=Join-Path ([IO.Path]::GetDirectoryName($payload)) 'backup'
    $files=@(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object {
        $rel=$_.FullName.Substring($payload.Length+1)
        $rel -notmatch '(^|\\)(?i:cache|saves|saves_bkp|backup|update-backups)(\\|$)' -and
        $rel -notmatch '^(?i:Setting.*|Save.*\.lps|disable-auto-updates.txt|startup-error.log|rimuru-update-.*\.json(?:\.tmp)?)$' -and
        ($rel -notmatch '^mod\\' -or $rel -match '^mod\\(0000_core|0100_rimuru)\\')
    })
    $changed=[Collections.Generic.List[object]]::new()
    try {
        foreach ($file in $files) {
            $relative=$file.FullName.Substring($payload.Length+1)
            $destination=Join-Path $targetRoot $relative
            # Refuse junctions/symlinks anywhere on the destination path.
            $parent=$destination
            while ($parent -and $parent.Length -ge $targetRoot.TrimEnd('\').Length) {
                if ((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Update destination contains a link.' }
                $parent=[IO.Path]::GetDirectoryName($parent)
            }
            $saved=Join-Path $backup $relative
            $existed=Test-Path -LiteralPath $destination
            if ($existed) {
                New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($saved)) | Out-Null
                Copy-Item -LiteralPath $destination -Destination $saved
            }
            $changed.Add(@{destination=$destination;saved=$saved;existed=$existed})
            New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    } catch {
        for ($i=$changed.Count-1;$i -ge 0;$i--) {
            $item=$changed[$i]
            try {
                if ($item.existed) { Copy-Item -LiteralPath $item.saved -Destination $item.destination -Force }
                elseif (Test-Path -LiteralPath $item.destination) { Remove-Item -LiteralPath $item.destination -Force }
            } catch { Write-Warning "Could not restore $($item.destination). Backup: $backup" }
        }
        throw
    }
    return $backup
}

function Install-RimuruPackage([string]$Package,[string]$Target,[string]$ExpectedVersion) {
    Install-RimuruPayload (Expand-RimuruPackage $Package $ExpectedVersion) $Target
}

function ConvertTo-RimuruArgument([string]$Value) {
    $escaped=[regex]::Replace($Value,'(\\*)"','$1$1\"')
    '"'+[regex]::Replace($escaped,'(\\+)$','$1$1')+'"'
}

function Get-RimuruProcesses([string]$App) {
    $exe=Join-Path $App 'VPet-Simulator.Windows.exe'
    @(Get-Process -Name 'VPet-Simulator.Windows' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
}

function Request-RimuruShutdown([string]$App,[string]$Version,$Plans,$Requests) {
    $pets=@(Get-RimuruProcesses $App)
    if (-not $pets.Count) { return }
    $token=[guid]::NewGuid().ToString('N')
    foreach ($pet in $pets) {
        $path=Join-Path $App "rimuru-update-$($pet.Id).request.json"
        $Requests.Add($path)
        @{Token=$token;Version=$Version;HelperPid=$PID} | ConvertTo-Json | Set-Content -LiteralPath ($path+'.tmp') -Encoding utf8
        Move-Item -LiteralPath ($path+'.tmp') -Destination $path -Force
    }
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $confirmed=@{}
    do {
        foreach ($pet in $pets) {
            if ($confirmed.ContainsKey($pet.Id)) { continue }
            $ack=Join-Path $App "rimuru-update-$($pet.Id).ack.json"
            if (-not (Test-Path -LiteralPath $ack)) { continue }
            $reply=Get-Content -LiteralPath $ack -Raw | ConvertFrom-Json
            if ($reply.Token -ne $token -or $reply.Version -ne $Version -or $reply.HelperPid -ne $PID) { continue }
            if (-not $reply.Saved) { throw 'The application could not save; automatic restart was cancelled.' }
            $confirmed[$pet.Id]=$true
            $Plans.Add(@{Id=$pet.Id;Arguments=@($reply.Arguments)})
        }
        if ($clock.Elapsed.TotalSeconds -gt 120) { throw 'Save/shutdown confirmation timed out; the updater will not force-close the pet.' }
        Start-Sleep -Milliseconds 250
    } until ($confirmed.Count -eq $pets.Count -and @(Get-RimuruProcesses $App).Count -eq 0)
}

function Restart-RimuruPlans([string]$App,$Plans) {
    foreach ($plan in $Plans) {
        if (@(Get-RimuruProcesses $App | Where-Object { $_.Id -eq $plan.Id }).Count) { continue }
        $exe=Join-Path $App 'VPet-Simulator.Windows.exe'
        if ($plan.Arguments.Count) {
            $arguments=($plan.Arguments | ForEach-Object { ConvertTo-RimuruArgument ([string]$_) }) -join ' '
            Start-Process -FilePath $exe -WorkingDirectory $App -ArgumentList $arguments | Out-Null
        } else { Start-Process -FilePath $exe -WorkingDirectory $App | Out-Null }
    }
}

if ($FunctionsOnly) { return }
$app=[IO.Path]::GetFullPath($AppDirectory)
if (Test-Path -LiteralPath (Join-Path $app 'disable-auto-updates.txt')) { exit }
$hasher=[Security.Cryptography.SHA256]::Create()
$key=[BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($app.ToLowerInvariant())))
$hasher.Dispose()
$mutex=[Threading.Mutex]::new($false,'Local\RimuruUpdate-'+$key)
$locked=$false
$plans=[Collections.Generic.List[object]]::new()
$requests=[Collections.Generic.List[string]]::new()
$backoff=Join-Path $app 'rimuru-update-failed.json'
try {
    $locked=$mutex.WaitOne(0)
    if (-not $locked) { exit }
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    $release=Invoke-RestMethod 'https://api.github.com/repos/TheApple05/VPetR/releases/latest' -Headers @{'User-Agent'='Rimuru-Updater'} -TimeoutSec 30
    if ($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^rimuru-v(\d+\.\d+\.\d+)$') { exit }
    $version=$Matches[1]
    $current=(Get-Content -LiteralPath (Join-Path $app 'rimuru-version.txt') -Raw).Trim()
    if ([version]$version -le [version]$current) {
        "Up to date: $current" | Set-Content -LiteralPath (Join-Path $app 'rimuru-update.log')
        exit
    }
    if (Test-Path -LiteralPath $backoff) {
        try {
            $failed=Get-Content -LiteralPath $backoff -Raw | ConvertFrom-Json
            if ($failed.Version -eq $version -and [DateTime]::UtcNow -lt [DateTime]::Parse($failed.RetryAfter).ToUniversalTime()) { exit }
        } catch { }
    }
    $asset=@($release.assets | Where-Object { $_.name -eq 'Rimuru-VPet-Portable-win-x64.zip' })
    if ($asset.Count -ne 1 -or $asset[0].digest -notmatch '^sha256:([0-9a-f]{64})$') { throw 'Missing update checksum.' }
    $hash=$Matches[1]
    if ($asset[0].size -gt 512MB) { throw 'Update download is too large.' }
    $url=$asset[0].browser_download_url
    if (-not $url.StartsWith('https://github.com/TheApple05/VPetR/releases/download/'+$release.tag_name+'/')) { throw 'Unexpected update source.' }
    $download=Join-Path ([IO.Path]::GetTempPath()) ('Rimuru-download-'+[guid]::NewGuid()+'.zip')
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $download -TimeoutSec 600
    if ((Get-RimuruFileHash $download) -ne $hash) { throw 'Update checksum mismatch.' }
    $payload=Expand-RimuruPackage $download $version
    Request-RimuruShutdown $app $version $plans $requests
    $backup=Install-RimuruPayload $payload $app
    Remove-Item -LiteralPath $download
    if (Test-Path -LiteralPath $backoff) { Remove-Item -LiteralPath $backoff }
    Restart-RimuruPlans $app $plans
    "Installed $version. Restarted $($plans.Count) pet process(es). Previous application files: $backup" | Set-Content -LiteralPath (Join-Path $app 'rimuru-update.log')
} catch {
    $message=$_.Exception.Message
    try {
        @{Version=$version;RetryAfter=[DateTime]::UtcNow.AddHours(6).ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath $backoff
        Restart-RimuruPlans $app $plans
        $message | Set-Content -LiteralPath (Join-Path $app 'rimuru-update.log')
    } catch { }

} finally {
    foreach ($request in $requests) {
        foreach ($path in $request,($request -replace '\.request\.json$','.ack.json')) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue }
        }
    }
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
