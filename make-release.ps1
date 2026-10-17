$ErrorActionPreference = 'Stop'
$env:PATH = 'C:\Program Files\Git\cmd;' + $env:PATH
# Токен берём из хранилища Git Credential Manager (не сохраняем на диск)
$cred = 'protocol=https' + "`n" + 'host=github.com' + "`n" + "`n" | git credential fill
$token = ($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=', ''
if (-not $token) { throw 'GitHub token not found in credential manager' }
$hdr = @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json' }

$notes = @'
## CavaWin v1.0.0 :musical_note:

First public release! A real-time audio spectrum visualizer for Windows 10/11 - it dances to whatever sound **your system is playing** via WASAPI loopback. No microphone needed.

### Five visualization styles
- **Bars** - classic cava rounded bars with falling peak caps
- **Monstercat waves** - solid smoothed spectrum with a glowing crest line
- **Mirror** - bars radiating symmetrically from the center
- **Oscilloscope** - the actual waveform of the sound, live
- **LED segments** - retro equalizer with hold-then-fall peak cells

### Themes
Cappuccino / Dracula / Neon - plus custom gradient colors

### How to run
1. Download `CavaWin-v1.0.0-win-x64.zip` below and unzip it
2. Run `cava.exe` - it is **self-contained**, no .NET runtime needed
3. If SmartScreen shows a warning: "More info" -> "Run anyway" (open-source, not code-signed)
4. Right-click the window for settings (style, theme, bars, FPS, opacity...)

### Features
- Zero-config WASAPI loopback capture with automatic device switching
- 4096-point FFT, logarithmic bands 50 Hz - 10 kHz, auto-gain
- Gravity physics, peak caps, neighbor-band smoothing
- Borderless draggable window, click-through mode, always-on-top
- Fullscreen mode (F11 / Esc) and tray icon
- All settings persisted to `%APPDATA%\CavaWin\config.json`

Full documentation in the [README](https://github.com/zaglotus4000/CAVA-For-Windows-#readme).
'@

$body = @{
    tag_name         = 'v1.0.0'
    target_commitish = 'main'
    name             = 'CavaWin v1.0.0'
    body             = $notes
    draft            = $false
    prerelease       = $false
} | ConvertTo-Json

$bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
$rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/zaglotus4000/CAVA-For-Windows-/releases' `
    -Method Post -Headers $hdr -Body $bytes -ContentType 'application/json'
Write-Output ("RELEASE OK id=" + $rel.id + " tag=" + $rel.tag_name)

$up = $rel.upload_url -replace '\{.*\}', ''
$zip = Join-Path $env:TEMP 'CavaWin-release\CavaWin-v1.0.0-win-x64.zip'
$asset = Invoke-RestMethod -Uri ($up + '?name=CavaWin-v1.0.0-win-x64.zip') `
    -Method Post -Headers $hdr -InFile $zip -ContentType 'application/zip'
Write-Output ("ASSET OK name=" + $asset.name + " size=" + $asset.size + " state=" + $asset.state)
Write-Output ("URL " + $asset.browser_download_url)