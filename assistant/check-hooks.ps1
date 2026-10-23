$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

$expected = [ordered]@{
    'Plain Craft Launcher 2/Program.cs'                                            = 1
    'Plain Craft Launcher 2/Modules/Minecraft/RestrictedFeaturePolicy.cs'           = 2
    'Plain Craft Launcher 2/FormMain.xaml'                                          = 1
    'Plain Craft Launcher 2/FormMain.xaml.cs'                                       = 5
    'Plain Craft Launcher 2/Pages/PageSetup/PageSetupLeft.xaml'                     = 1
    'Plain Craft Launcher 2/Pages/PageSetup/PageSetupLeft.xaml.cs'                  = 1
    'README.md'                                                                     = 1
}

$failed = $false
foreach ($entry in $expected.GetEnumerator()) {
    $path = $entry.Key -replace '/', [IO.Path]::DirectorySeparatorChar
    if (-not (Test-Path $path)) {
        Write-Host "MISSING FILE  $($entry.Key)" -ForegroundColor Red
        $failed = $true
        continue
    }
    $actual = @(Select-String -Path $path -Pattern '[ASSISTANT]' -SimpleMatch).Count
    if ($actual -ne $entry.Value) {
        $color = if ($actual -lt $entry.Value) { 'Red' } else { 'Yellow' }
        Write-Host ("HOOK MISMATCH {0}: expected {1}, found {2}" -f $entry.Key, $entry.Value, $actual) -ForegroundColor $color
        $failed = $true
    } else {
        Write-Host ("hook ok  {0}  ({1})" -f $entry.Key, $actual) -ForegroundColor DarkGreen
    }
}

# 上游若新增了 fork 未登记的哨兵，说明有人把私有代码写进了上游文件
$extra = Get-ChildItem -Recurse -Include *.cs, *.xaml |
    Where-Object { $_.FullName -notmatch '[\\/]Assistant[\\/]' } |
    Select-String -Pattern '[ASSISTANT]' -SimpleMatch |
    ForEach-Object { ($_.Path -replace [regex]::Escape((Get-Location).Path + [IO.Path]::DirectorySeparatorChar), '') -replace '\\', '/' } |
    Sort-Object -Unique
foreach ($file in $extra) {
    if (-not $expected.Contains($file)) {
        Write-Host "UNREGISTERED HOOK FILE  $file  (把它加进 assistant/SYNC.md 与本脚本)" -ForegroundColor Yellow
        $failed = $true
    }
}

if ($failed) {
    Write-Host 'fork hooks 校验未通过' -ForegroundColor Red
    exit 1
}
Write-Host 'fork hooks 校验通过' -ForegroundColor Green
