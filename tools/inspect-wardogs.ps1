# Read-only report on how Wardogs stores its files, to decide what the mashup can read.
# It never starts Wardogs and never writes into its folder. Prints a text report.
# Usage: powershell -ExecutionPolicy Bypass -File inspect-wardogs.ps1

$ErrorActionPreference = 'Stop'
$AppId = '1867240'

function Find-SteamRoot {
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam') {
        try {
            $p = Get-ItemProperty -Path $key -ErrorAction Stop
            foreach ($name in 'SteamPath', 'InstallPath') {
                if ($p.$name -and (Test-Path $p.$name)) { return $p.$name }
            }
        } catch { }
    }
    return $null
}

function Find-Wardogs {
    $steam = Find-SteamRoot
    if (-not $steam) { return $null }
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    $libraries = @($steam)
    if (Test-Path $vdf) {
        foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
            $libraries += $m.Groups[1].Value -replace '\\\\', '\'
        }
    }
    foreach ($lib in $libraries | Select-Object -Unique) {
        $acf = Join-Path $lib "steamapps\appmanifest_$AppId.acf"
        if (Test-Path $acf) {
            $dir = [regex]::Match((Get-Content $acf -Raw), '"installdir"\s+"([^"]+)"').Groups[1].Value
            $full = Join-Path $lib "steamapps\common\$dir"
            if (Test-Path $full) { return $full }
        }
    }
    return $null
}

function Read-Tail([string]$path, [int]$count) {
    $fs = [System.IO.File]::OpenRead($path)
    try {
        $n = [Math]::Min($count, $fs.Length)
        $fs.Seek(-$n, 'End') | Out-Null
        $buf = New-Object byte[] $n
        [void]$fs.Read($buf, 0, $n)
        return $buf
    } finally { $fs.Close() }
}

function Read-Head([string]$path, [int]$count) {
    $fs = [System.IO.File]::OpenRead($path)
    try {
        $buf = New-Object byte[] $count
        [void]$fs.Read($buf, 0, $count)
        return $buf
    } finally { $fs.Close() }
}

# .pak footer: [key guid 16][encrypted index 1][magic E1 12 6F 5A][version 4]...
function Describe-Pak([string]$path) {
    $tail = Read-Tail $path 1024
    for ($i = $tail.Length - 4; $i -ge 17; $i--) {
        if ($tail[$i] -eq 0xE1 -and $tail[$i + 1] -eq 0x12 -and $tail[$i + 2] -eq 0x6F -and $tail[$i + 3] -eq 0x5A) {
            $version = [BitConverter]::ToInt32($tail, $i + 4)
            $encIndex = $tail[$i - 1]
            $guidZero = -not ($tail[($i - 17)..($i - 2)] | Where-Object { $_ -ne 0 })
            return "pak v$version encryptedIndex=$encIndex keyGuidZero=$guidZero"
        }
    }
    return 'pak footer not found'
}

# .utoc header: magic(16) ... ContainerFlags at byte 80 (1=Compressed 2=Encrypted 4=Signed 8=Indexed)
function Describe-Utoc([string]$path) {
    $h = Read-Head $path 96
    $magic = [System.Text.Encoding]::ASCII.GetString($h, 0, 16)
    if ($magic -ne '-==--==--==--==-') { return 'utoc magic not found' }
    $flags = $h[80]
    $guidZero = -not ($h[64..79] | Where-Object { $_ -ne 0 })
    return "utoc v$($h[16]) flags=$flags encrypted=$([bool]($flags -band 2)) keyGuidZero=$guidZero"
}

$root = Find-Wardogs
if (-not $root) { Write-Output 'WARDOGS NOT FOUND in any Steam library.'; exit 1 }
Write-Output "Wardogs folder: $root"
$acfVersion = Get-ChildItem -Path $root -Filter '*.exe' -Recurse -Depth 3 -ErrorAction SilentlyContinue |
    Select-Object -First 5 | ForEach-Object { "$($_.FullName.Substring($root.Length)) $($_.VersionInfo.FileVersion)" }
Write-Output "Executables:"; $acfVersion | ForEach-Object { Write-Output "  $_" }

$files = Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue
Write-Output "`nFile types (count, MB):"
$files | Group-Object Extension | Sort-Object { ($_.Group | Measure-Object Length -Sum).Sum } -Descending |
    Select-Object -First 25 | ForEach-Object {
        $mb = [Math]::Round((($_.Group | Measure-Object Length -Sum).Sum) / 1MB, 1)
        Write-Output ("  {0,-10} {1,6} {2,10}" -f $_.Name, $_.Count, $mb)
    }

Write-Output "`nFolders (two levels):"
Get-ChildItem -Path $root -Directory -Recurse -Depth 1 | ForEach-Object { Write-Output "  $($_.FullName.Substring($root.Length))" }

Write-Output "`nContainers:"
foreach ($f in $files | Where-Object { $_.Extension -in '.pak', '.utoc' } | Sort-Object Length -Descending | Select-Object -First 40) {
    $desc = if ($f.Extension -eq '.pak') { Describe-Pak $f.FullName } else { Describe-Utoc $f.FullName }
    Write-Output ("  {0} ({1} MB): {2}" -f $f.FullName.Substring($root.Length), [Math]::Round($f.Length / 1MB), $desc)
}

Write-Output "`nLoose audio and text (first 30 each):"
foreach ($ext in '.wem', '.bnk', '.ogg', '.wav', '.bank', '.locres', '.json', '.csv') {
    $hits = $files | Where-Object { $_.Extension -eq $ext }
    if ($hits) {
        Write-Output "  $ext : $($hits.Count) files"
        $hits | Select-Object -First 30 | ForEach-Object { Write-Output "    $($_.FullName.Substring($root.Length))" }
    }
}
