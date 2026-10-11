"""Check shipped PCM, headroom, silence, fades and distinct foley variants."""
import array
import hashlib
import json
import math
from pathlib import Path
import wave

mod=Path(__file__).resolve().parents[1]
manifest=json.loads((mod/'audio/manifest.json').read_text(encoding='utf-8'))
errors=[]; variants={}; report=[]
for name in [*manifest['assets'],'drum']:
    path=mod/'audio'/f'{name}.wav'
    with wave.open(str(path),'rb') as sound:
        raw=sound.readframes(sound.getnframes())
        pcm=array.array('h',raw)
        if (sound.getnchannels(),sound.getsampwidth(),sound.getframerate())!=(1,2,22050): errors.append(f'{name}: incompatible PCM format')
        seconds=len(pcm)/sound.getframerate()
    peak=max(abs(v) for v in pcm)/32767
    rms=math.sqrt(sum((v/32767)**2 for v in pcm)/len(pcm))
    if not .05<=seconds<=12: errors.append(f'{name}: invalid effect duration')
    if not .01<=rms<=.4 or peak>=.99: errors.append(f'{name}: silent or clipped audio')
    if name!='drum' and (pcm[0]!=0 or pcm[-1]!=0): errors.append(f'{name}: missing edge fades')
    if name!='drum':
        kind=name.rsplit('-',1)[0]; digest=hashlib.sha256(raw).hexdigest()
        if digest in variants.setdefault(kind,set()): errors.append(f'{name}: duplicate variant')
        variants[kind].add(digest)
    report.append({'name':name,'seconds':round(seconds,3),'peak':round(peak,4),'rms':round(rms,4)})
if list((mod/'audio').glob('music*')): errors.append('Music was not requested and must not be shipped in the audio bank')
out=mod.parents[1]/'analysis/medieval/audio'
out.mkdir(parents=True,exist_ok=True)
(out/'assets.json').write_text(json.dumps({'passed':not errors,'errors':errors,'files':len(report),'assets':report},indent=2),encoding='utf-8')
if errors: raise SystemExit('\n'.join(errors))
print(f'Audio validation passed: {len(report)} mono PCM effects, distinct variants, no clipping or music.')
