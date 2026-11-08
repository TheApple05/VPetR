$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\Update-Rimuru.ps1') -FunctionsOnly
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=Join-Path ([IO.Path]::GetTempPath()) ('Rimuru-update-tests-'+[guid]::NewGuid())
New-Item -ItemType Directory -Path $root | Out-Null
function Assert($Condition,$Message) { if (-not $Condition) { throw $Message } }
function Package($Name,$Extra=@{},$Version='9.0.0') {
    $path=Join-Path $root ($Name+'.zip')
    $zip=[IO.Compression.ZipFile]::Open($path,'Create')
    try {
        $entries=@{'VPet-Simulator.Windows.exe'='new-exe';'mod/0000_core/info.lps'='core';'mod/0100_rimuru/info.lps'='rimuru';'rimuru-version.txt'=$Version;'Setting.lps'='overwrite';'Saves/save.lps'='overwrite';'mod/custom/user.lps'='overwrite'}
        foreach ($key in $Extra.Keys) { $entries[$key]=$Extra[$key] }
        foreach ($key in $entries.Keys) {
            $entry=$zip.CreateEntry('Rimuru-VPet/'+$key)
            $writer=[IO.StreamWriter]::new($entry.Open());$writer.Write($entries[$key]);$writer.Dispose()
        }
    } finally { $zip.Dispose() }
    return $path
}
$target=Join-Path $root 'app'
New-Item -ItemType Directory -Force -Path $target,(Join-Path $target 'Saves'),(Join-Path $target 'mod/custom') | Out-Null
'original-setting' | Set-Content (Join-Path $target 'Setting.lps')
'original-save' | Set-Content (Join-Path $target 'Saves/save.lps')
'original-mod' | Set-Content (Join-Path $target 'mod/custom/user.lps')
$null=Install-RimuruPackage (Package 'good') $target '9.0.0'
Assert ((Get-Content (Join-Path $target 'VPet-Simulator.Windows.exe')) -eq 'new-exe') 'Update did not install.'
Assert ((Get-Content (Join-Path $target 'Setting.lps')) -eq 'original-setting') 'Settings were overwritten.'
Assert ((Get-Content (Join-Path $target 'Saves/save.lps')) -eq 'original-save') 'Save was overwritten.'
Assert ((Get-Content (Join-Path $target 'mod/custom/user.lps')) -eq 'original-mod') 'Custom mod overwritten.'
foreach ($case in 'traversal','wrong-version') {
    $package=if ($case -eq 'traversal') { Package $case @{'../escaped.txt'='bad'} } else { Package $case @{} '8.0.0' }
    $failed=$false
    try { $null=Install-RimuruPackage $package $target '9.0.0' } catch { $failed=$true }
    Assert $failed "Accepted $case"
}
Assert (-not (Test-Path (Join-Path $root 'escaped.txt'))) 'ZIP traversal escaped.'
'old-a' | Set-Content (Join-Path $target 'a.dll')
'old-z' | Set-Content (Join-Path $target 'z.dll')
$locked=[IO.File]::Open((Join-Path $target 'z.dll'),'Open','Read','Read')
try {
    $failed=$false
    try { $null=Install-RimuruPackage (Package 'locked' @{'a.dll'='new-a';'z.dll'='new-z'} '10.0.0') $target '10.0.0' } catch { $failed=$true }
    Assert $failed 'Locked file should prevent installation.'
    Assert ((Get-Content (Join-Path $target 'a.dll')) -eq 'old-a') 'Rollback did not restore previous file.'
    Assert ((Get-Content (Join-Path $target 'rimuru-version.txt')) -eq '9.0.0') 'Rollback changed the installed version.'
} finally { $locked.Dispose() }
Write-Output 'Passed: install, settings/save/mod preservation, ZIP traversal, version mismatch, locked-file rollback.'
