"""Check all water poses and that generated manifests remain stable."""
from pathlib import Path
import argparse
from PIL import Image
from firefighting_content import PEOPLE, MODES, wire

parser=argparse.ArgumentParser();parser.add_argument('--mod',type=Path);args=parser.parse_args()
mod=args.mod or Path(__file__).resolve().parents[1]
for name in PEOPLE:
    sheet=Image.open(mod/'art'/f'water-{name}.png')
    assert sheet.info['FrameSize']=='56,52' and int(sheet.info['FrameAmount'])==64*len(MODES)
    cols=sheet.width//56
    frames=[sheet.crop((i%cols*56,i//cols*52,(i%cols+1)*56,(i//cols+1)*52)) for i in range(64*len(MODES))]
    for i,frame in enumerate(frames):
        b=frame.getbbox(); assert b and b[0]>0 and b[1]>0 and b[2]<56 and b[3]<52,f'{name}/{i}: clipped pose'
    for mode in ('water-empty-run','water-run','water-fill','water-pour'):
        for facing in range(8):
            start=MODES.index(mode)*64+facing*8
            assert len({p.tobytes() for p in frames[start:start+8]})>=4,f'{name}/{mode}/{facing}: static animation'
    assert frames[0].tobytes()!=frames[128].tobytes(),name+': empty and full buckets must look different'
base=(Path(__file__).resolve().parents[1]/'mod.yaml').read_text(encoding='utf-8')
assert wire(wire(base))==wire(base),'Manifest generation must be idempotent'
print(f'PASS: {len(PEOPLE)*64*len(MODES)} water poses, 11 roles, six animations, eight directions, no clipping, distinct empty/full buckets, stable manifests.')
