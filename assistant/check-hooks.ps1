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

# 哨兵之外还有一道硬检查：相对 upstream/dev，私有目录以外被"改"的上游文件必须都在登记表里。
# 新增文件（A）不影响 merge，只统计；改动/删除上游文件（M/D/R）没登记就是给下次同步埋坑
$privatePrefixes = @(
    '^PCL\.Core/Assistant/',
    '^PCL\.Core\.Test/Assistant/',
    '^Plain Craft Launcher 2/Assistant/',
    '^assistant/'
)
$upstreamRef = (@(& git rev-parse --verify -q 'upstream/dev') | Out-String).Trim()
if ($LASTEXITCODE -eq 0 -and $upstreamRef) {
    $rows = @(& git diff --name-status 'upstream/dev...HEAD' 2>$null | Where-Object { $_ })
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'SKIP  无法与 upstream/dev 比较（shallow clone？请在 pclce-full 里跑本脚本）' -ForegroundColor Yellow
    } else {
        $added = 0
        $modified = 0
        $stray = @()
        foreach ($row in $rows) {
            $parts = $row -split "`t"
            $status = $parts[0]
            $path = $parts[-1]
            if ($privatePrefixes | Where-Object { $path -match $_ }) { continue }
            if ($status -like 'A*') { $added++; continue }
            $modified++
            if (-not $expected.Contains($path)) { $stray += "$status $path" }
        }
        foreach ($file in $stray) {
            Write-Host "UPSTREAM EDIT WITHOUT SENTINEL  $file" -ForegroundColor Red
            $failed = $true
        }
        Write-Host ("parity  upstream/dev@{0}：上游文件改动 {1} 个（登记表 {2} 个 / 哨兵 {3} 处），未登记 {4} 个，fork 新增文件 {5} 个" -f `
            $upstreamRef.Substring(0, 7), $modified, $expected.Count, (($expected.Values | Measure-Object -Sum).Sum),
            $stray.Count, $added) -ForegroundColor DarkCyan
        if ($stray.Count -eq 0) { Write-Host 'parity ok  上游文件改动全部在登记表内' -ForegroundColor DarkGreen }
    }
} else {
    Write-Host 'SKIP  本地没有 upstream/dev 引用，只做了哨兵检查' -ForegroundColor Yellow
}

if ($failed) {
    Write-Host 'fork hooks 校验未通过' -ForegroundColor Red
    exit 1
}
Write-Host 'fork hooks 校验通过' -ForegroundColor Green
