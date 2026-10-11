"""Validate the shipped pose sheets, proportions, directions and capture stages."""
from pathlib import Path
import argparse
from PIL import Image
from livestock_art import KINDS, MODES
from livestock_visuals_content import GUIDES
from animation_art import HERD_MODES

parser=argparse.ArgumentParser();parser.add_argument('--mod',type=Path);args=parser.parse_args()
mod=args.mod or Path(__file__).resolve().parents[1]

def frames(name,count,size):
    sheet=Image.open(mod/'art'/f'{name}.png')
    assert sheet.mode=='RGBA' and sheet.info['FrameSize']==','.join(map(str,size)),name
    assert int(sheet.info['FrameAmount'])==count,name
    w,h=size;cols=sheet.width//w
    result=[sheet.crop(((i%cols)*w,(i//cols)*h,(i%cols+1)*w,(i//cols+1)*h)) for i in range(count)]
    for i,pose in enumerate(result):
        b=pose.getbbox()
        assert b and b[0]>0 and b[1]>0 and b[2]<w and b[3]<h,f'{name}/{i}: clipped pose {b}'
    return result

all_poses={}
for kind in KINDS:
    poses=frames('herd-'+kind,64*len(MODES),(64,56));all_poses[kind]=poses
    assert len({poses[f*8].tobytes() for f in range(8)})==8,f'{kind}: direction missing'
    for mode in ('walk','graze','led'):
        start=MODES.index(mode)*64
        for facing in range(8):
            assert len({p.tobytes() for p in poses[start+facing*8:start+facing*8+8]})>=6,f'{kind}/{mode}/{facing}: motion missing'
    for facing in range(8):
        assert poses[facing*8].tobytes()==poses[128+facing*8].tobytes(),f'{kind}: head jumps at start of grazing'
for young,adult in (('calf','cow'),('lamb','sheep')):
    for f in range(8):
        def solid(p):return p.getchannel('A').point(lambda x:255 if x>128 else 0).getbbox()
        a,b=solid(all_poses[adult][f*8]),solid(all_poses[young][f*8])
        assert b[3]-b[1]<a[3]-a[1],f'{young}: adult and young must be distinguishable'
        assert abs(a[3]-b[3])<=3,f'{young}: hooves must remain on the same ground plane'
for name in GUIDES:
    poses=frames('herding-'+name,64*len(HERD_MODES),(56,52))
    for mode in range(3):
        for f in range(8):
            assert len({p.tobytes() for p in poses[mode*64+f*8:mode*64+f*8+8]})>=(2 if mode==2 else 4),f'{name}: missing gesture or walk'
frames('shepherd',640,(56,52))
print(f'PASS: {len(KINDS)*len(MODES)*64} animal poses, {len(GUIDES)*len(HERD_MODES)*64} guide poses; no clipped frames; young proportions, ground anchors, grazing transition and all eight facings checked.')
