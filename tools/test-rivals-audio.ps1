# Tries to pull the sound out of a few of Marvel Rivals' loose Bink videos.
# Read-only for the game: writes short .wav clips to %TEMP%\rivals-audio-test only.
param([string]$Root)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$ffmpeg = Join-Path $here 'bin\ffmpeg.exe'
$ffprobe = Join-Path $here 'bin\ffprobe.exe'
. (Join-Path $here 'inspect-game.ps1') -AppId 2767030 -FunctionsOnly
# Native tools write progress to stderr; that must not stop the script.
$ErrorActionPreference = 'Continue'
if (-not $Root) { $Root = Find-Game }
if (-not $Root) { Write-Output 'Marvel Rivals not found.'; exit 1 }
$movies = Join-Path $Root 'MarvelGame\Marvel\Content\Marvel\MoviesBink'
$out = Join-Path $env:TEMP 'rivals-audio-test'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$samples = @(
    'Movies\LoginAndLobby\S10\LobbyLoop.bk2',
    'Movies_Level\Loading\5003\Loading_Tokyo.bk2',
    'Movies_Level\LevelEntrance\5003\TojyoE01_Video.bk2',
    'Movies_Level\LevelExit\5005\KlyntarH01_Attack_Video.bk2',
    'Movies\Mall\Gift\Gift_05.bk2',
    'Activity\151\S5_Activity151_M2201RewardedHoverLoopMask.bk2'
)
foreach ($rel in $samples) {
    $src = Join-Path $movies $rel
    Write-Output "`n=== $rel"
    if (-not (Test-Path $src)) { Write-Output '  (file not present)'; continue }
    $head = Read-Head $src 64
    Write-Output ('  header: ' + (($head | ForEach-Object { $_.ToString('x2') }) -join ' '))
    & $ffprobe -hide_banner -v error -show_entries 'stream=index,codec_type,codec_name,sample_rate,channels:format=duration' -of compact $src 2>&1 | ForEach-Object { "  probe: $_" }
    $wav = Join-Path $out (([IO.Path]::GetFileNameWithoutExtension($src)) + '.wav')
    & $ffmpeg -hide_banner -v error -y -i $src -map 0:a:0 -t 20 -af volumedetect -c:a pcm_s16le $wav 2>&1 | Select-Object -First 12 | ForEach-Object { "  ffmpeg: $_" }
    & $ffmpeg -hide_banner -nostats -i $wav -af volumedetect -f null NUL 2>&1 | Select-String 'mean_volume|max_volume|Duration' | ForEach-Object { "  level: $($_.Line.Trim())" }
    if (Test-Path $wav) { Write-Output ("  wrote {0} ({1} KB)" -f $wav, [Math]::Round((Get-Item $wav).Length / 1KB)) }
}
Write-Output "`nClips are in $out. Play one to hear whether it is real Rivals sound."
