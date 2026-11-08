param([switch]$UseStandardAtlas,[string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $taskRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sdk = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) {
    $sdk = (Get-Command dotnet -ErrorAction Stop).Source
}
$atlasName = if ($UseStandardAtlas) { 'spritesheet-standard.png' } else { 'spritesheet-extended.png' }
$atlas = Join-Path $taskRoot "Library\Pets\Rimuru\final\$atlasName"
& python (Join-Path $taskRoot 'scripts\export_vpet.py') --atlas $atlas
if ($LASTEXITCODE -ne 0) { throw 'Character export failed.' }
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot 'dist\Rimuru-VPet' }
& $sdk publish (Join-Path $repoRoot 'VPet-Simulator.Windows\VPet-Simulator.Windows.csproj') -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $output --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'VPet publish failed.' }
$coreSource = Join-Path $repoRoot 'VPet-Simulator.Windows\mod\0000_core'
$coreOutput = Join-Path $output 'mod\0000_core'
New-Item -ItemType Directory -Path $coreOutput -Force | Out-Null
foreach ($folder in 'food','image','lang','text','theme','photo','file') {
    $source = Join-Path $coreSource $folder
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $coreOutput -Recurse -Force }
}
Copy-Item -LiteralPath (Join-Path $coreSource 'info.lps') -Destination $coreOutput -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'VPet-Simulator.Windows\mod\0100_rimuru') -Destination (Join-Path $output 'mod') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $output 'LICENSE-VPet.txt') -Force
$settingsPath = Join-Path $output 'Setting.lps'
if (-not (Test-Path -LiteralPath $settingsPath)) {
    @'
Setting#VPET:|
language#en:|
gameconfig:|petgraph#rimuru:|
SingleTips:|tutorial#2026/11/03:|helloworld#True:|
'@ | Set-Content -LiteralPath $settingsPath -Encoding utf8
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'Launch-Rimuru.cmd') -Destination $output -Force
Write-Host "Ready: $output"
