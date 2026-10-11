# Generate the shipped French synthetic speech using installed Windows voices.
# Runtime playback uses only the WAV files; players need no TTS installation.
param([string]$Python = 'C:\Program Files\Python314\python.exe')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$modRoot = Split-Path $PSScriptRoot -Parent
$rawRoot = Join-Path $modRoot '../../analysis/medieval/audio-expanded/voice-raw'
New-Item -ItemType Directory -Path $rawRoot -Force | Out-Null
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $synth.SelectVoice('Microsoft Paul')
    $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
    $lines = [ordered]@{
        'combat-cry-1' = 'En avant !'
        'combat-cry-2' = 'Pour le royaume !'
        'combat-cry-3' = 'À l''attaque !'
        'rally-1' = 'Tenez bon, soldats !'
        'rally-2' = 'Ralliez-vous à la bannière !'
        'rally-3' = 'Courage ! Gardez vos rangs !'
        'patrol-1' = 'Tout est calme. Continuons la ronde.'
        'patrol-2' = 'Ouvrez l''œil, camarades.'
        'patrol-3' = 'La garde veille.'
        'patrol-4' = 'Rien à signaler de ce côté.'
    }
    $entries = [ordered]@{}
    foreach ($line in $lines.GetEnumerator()) {
        $synth.Rate = if ($line.Key.StartsWith('combat')) { 2 } elseif ($line.Key.StartsWith('rally')) { 1 } else { -1 }
        $synth.SetOutputToWaveFile((Join-Path $rawRoot ($line.Key + '.wav')), $format)
        $synth.Speak($line.Value)
        $synth.SetOutputToNull()
        $entries[$line.Key] = @{ text = $line.Value; voice = 'Microsoft Paul'; source = 'Windows local speech synthesis' }
    }
    @{ assets = $entries } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $rawRoot 'lines.json') -Encoding UTF8
} finally { $synth.Dispose() }
& $Python (Join-Path $PSScriptRoot 'voice_audio.py') $rawRoot
if ($LASTEXITCODE -ne 0) { throw 'Voice mastering failed' }
