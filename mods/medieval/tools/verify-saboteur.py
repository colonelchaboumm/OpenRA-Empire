"""Validate every new pose and make sure chrome regeneration is idempotent."""
from pathlib import Path
import argparse
from PIL import Image
from saboteur_content import chrome
from animation_art import UNIT_MODES, HERD_MODES

parser=argparse.ArgumentParser();parser.add_argument('--mod',type=Path);args=parser.parse_args()
mod=args.mod or Path(__file__).resolve().parents[1]
modes=UNIT_MODES+HERD_MODES+('ignite','slaughter')
for name,count,size in [('saboteur',64*len(modes),(56,52)),('sabotage-fire',16,(96,128))]:
    sheet=Image.open(mod/'art'/f'{name}.png')
    assert sheet.mode=='RGBA' and sheet.info['FrameSize']==','.join(map(str,size))
    assert int(sheet.info['FrameAmount'])==count
    w,h=size;cols=sheet.width//w
    frames=[sheet.crop(((i%cols)*w,(i//cols)*h,(i%cols+1)*w,(i//cols+1)*h)) for i in range(count)]
    for i,frame in enumerate(frames):
        bounds=frame.getbbox()
        assert bounds and bounds[0]>0 and bounds[1]>0 and bounds[2]<w and bounds[3]<h,f'{name}/{i}: clipped {bounds}'
    if name=='saboteur':
        for mode in ('run','ignite','slaughter','catch','lead'):
            for f in range(8):
                start=modes.index(mode)*64+f*8
                assert len({p.tobytes() for p in frames[start:start+8]})>=4,f'{mode}/{f}: missing movement'
    else:assert len({p.tobytes() for p in frames})==16
text=(mod/'chrome/ingame-player.yaml').read_text(encoding='utf-8')
assert text.count('Background@MD_SABOTEUR_PANEL:')==1
assert text.count('Button@MD_BARRACKS_SABOTEUR:')==1
assert chrome(text)==text,'Chrome regeneration must not duplicate or move the panels'
previous=0
for number,line in enumerate(text.splitlines(),1):
    level=len(line)-len(line.lstrip('\t'))
    assert level<=previous+1,f'Chrome line {number}: invalid MiniYaml indentation after preceding line'
    previous=level
print('PASS: 960 saboteur poses, 16 fire frames, eight directions, no clipped sprites; single stable recruitment/action panels.')
