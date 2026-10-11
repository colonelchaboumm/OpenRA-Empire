"""Master locally synthesized speech to the same mono format/headroom as game foley."""
import array
import json
import math
from pathlib import Path
import sys
import wave
from village_audio import RATE, OUT, REPORT, save


def main(raw_root, manifest='voices-manifest.json'):
    raw = Path(raw_root)
    metadata = json.loads((raw/'lines.json').read_text(encoding='utf-8-sig'))
    for name, entry in metadata['assets'].items():
        with wave.open(str(raw/(name+'.wav')), 'rb') as sound:
            assert (sound.getnchannels(), sound.getsampwidth(), sound.getframerate()) == (1, 2, RATE)
            samples = [v/32768 for v in array.array('h', sound.readframes(sound.getnframes()))]
        audible = [i for i, v in enumerate(samples) if abs(v) > .003]
        assert audible, name
        samples = samples[max(0, audible[0]-220):min(len(samples), audible[-1]+1100)]
        # Small pitch variations and mild saturation add body without hiding the words.
        ratio = (.96, 1.015, 1.04, .985)[int(name[-1])-1]
        output = []
        low = 0
        for i in range(int((len(samples)-1)/ratio)):
            at = i*ratio
            j = int(at)
            value = samples[j]*(1-at+j)+samples[j+1]*(at-j)
            low += .08*(value-low)
            output.append(math.tanh((value-.35*low)*(1.5 if name.startswith('patrol') else 2.0)))
        save(name, output, .60 if name.startswith('patrol') else .72)
        entry.update(REPORT[name])
    metadata.update(source='French synthetic voices; voice_audio.ps1 + voice_audio.py', sampleRate=RATE, channels=1, bits=16)
    (OUT/manifest).write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'Mastered {len(metadata["assets"])} French voice lines.')


if __name__ == '__main__':
    main(*sys.argv[1:])
