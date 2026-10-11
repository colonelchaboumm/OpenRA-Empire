# Offline French exclamations. The game plays the shipped PCM, not a TTS service.
param([string]$Python = 'C:\Program Files\Python314\python.exe')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$modRoot = Split-Path $PSScriptRoot -Parent
$rawRoot = Join-Path $modRoot '../../analysis/medieval/weather-polish/voice-raw'
New-Item -ItemType Directory -Path $rawRoot -Force | Out-Null
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $available = @($synth.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -eq 'fr-FR' })
    if ($available.Count -eq 0) { throw 'No local French speech voice is installed.' }
    $chosen = $available | Where-Object { $_.VoiceInfo.Name -eq 'Microsoft Paul' } | Select-Object -First 1
    if ($null -eq $chosen) { $chosen = $available[0] }
    $synth.SelectVoice($chosen.VoiceInfo.Name)
    $synth.Rate = 1
    $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
    $lines = [ordered]@{
        'storm-surprise-1' = 'Oh ! Quel fracas !'
        'storm-surprise-2' = 'Par les cieux !'
        'storm-surprise-3' = 'Ah ! Cet orage !'
    }
    $entries = [ordered]@{}
    foreach ($line in $lines.GetEnumerator()) {
        $synth.SetOutputToWaveFile((Join-Path $rawRoot ($line.Key + '.wav')), $format)
        $synth.Speak($line.Value)
        $synth.SetOutputToNull()
        $entries[$line.Key] = @{ text = $line.Value; voice = $chosen.VoiceInfo.Name; source = 'Windows local speech synthesis' }
    }
    @{ assets = $entries } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $rawRoot 'lines.json') -Encoding UTF8
} finally { $synth.Dispose() }
& $Python (Join-Path $PSScriptRoot 'voice_audio.py') $rawRoot weather-voices-manifest.json
if ($LASTEXITCODE -ne 0) { throw 'Weather voice mastering failed' }
