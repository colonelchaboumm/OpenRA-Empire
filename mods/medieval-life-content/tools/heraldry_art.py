"""Native pixel heraldry: six emblems, waving standards and empire-colored clothing."""
from pathlib import Path
import math
from PIL import Image, ImageDraw, PngImagePlugin

MOD = Path(__file__).resolve().parents[1]

def save(name, frames):
    w,h=frames[0].size; cols=min(16,len(frames))
    sheet=Image.new('RGBA',(w*cols,h*math.ceil(len(frames)/cols)))
    for i,frame in enumerate(frames): sheet.alpha_composite(frame,((i%cols)*w,(i//cols)*h))
    meta=PngImagePlugin.PngInfo();meta.add_text('FrameSize',f'{w},{h}');meta.add_text('FrameAmount',str(len(frames)))
    sheet.save(MOD/'art'/f'{name}.png',pnginfo=meta)

def emblem(index, size):
    # Draw at integer coordinates, then downsample to the cloth's pixel grid.
    im=Image.new('RGBA',(24,24));d=ImageDraw.Draw(im);gold='#fff0c1';edge='#664d2a'
    def poly(points):d.polygon(points,fill=gold,outline=edge)
    def line(points,width=3):d.line(points,fill=gold,width=width)
    if index==0:  # Rampant lion: raised paws, mane, bent hind legs and curled tail.
        poly([(9,9),(12,7),(15,10),(14,16),(18,19),(17,21),(13,18),(9,21),(6,20),(10,16)])
        d.ellipse((8,3,15,10),fill=edge);d.ellipse((10,4,15,9),fill=gold);d.point((14,6),fill=edge)
        line([(11,11),(6,10),(5,6)],2);line([(13,11),(18,9),(18,5)],2)
        line([(9,16),(5,15),(3,11),(4,7),(6,6)],2)
    elif index==1:  # Eagle with spread wings.
        poly([(11,8),(6,4),(1,3),(3,8),(6,10),(2,9),(5,13),(9,14),(8,20),(12,18),(16,20),(15,14),(19,13),(22,9),(18,10),(21,6),(23,3),(18,4),(13,8)])
        d.ellipse((10,4,14,10),fill=gold);poly([(13,5),(17,7),(13,8)])
    elif index==2:
        for i in range(8):
            a=i*math.pi/4;line([(12+round(math.cos(a)*7),12+round(math.sin(a)*7)),(12+round(math.cos(a)*11),12+round(math.sin(a)*11))],2)
        d.ellipse((6,6,18,18),fill=gold,outline=edge);d.point((10,11),fill=edge);d.point((14,11),fill=edge)
    elif index==3:
        poly([(5,21),(6,9),(4,9),(4,3),(8,3),(8,6),(10,6),(10,3),(14,3),(14,6),(16,6),(16,3),(20,3),(20,9),(18,9),(19,21)])
        d.rectangle((10,15,14,21),fill=edge);d.rectangle((10,10,13,12),fill=edge)
    elif index==4:
        line([(9,10),(6,6),(6,2)],2);line([(7,7),(2,5),(2,2)],2);line([(15,10),(18,6),(18,2)],2);line([(17,7),(22,5),(22,2)],2)
        poly([(7,10),(3,9),(6,14),(9,14),(10,20),(12,22),(14,20),(15,14),(18,14),(21,9),(17,10),(14,8),(10,8)])
        d.point((10,14),fill=edge);d.point((14,14),fill=edge)
    else:
        poly([(12,1),(16,6),(14,12),(18,6),(22,7),(22,12),(17,15),(14,15),(16,21),(12,18),(8,21),(10,15),(7,15),(2,12),(2,7),(6,6),(10,12),(8,6)])
        d.rectangle((7,13,17,15),fill=gold,outline=edge)
    return im.resize((size,size),Image.Resampling.NEAREST)

def cloth(im,x,y,width,height,phase,index):
    flag=Image.new('RGBA',(width,height));d=ImageDraw.Draw(flag)
    for u in range(width):
        shade=round(24*math.cos(phase-u*.28))
        d.line((u,0,u,height-1),fill=(196+shade,79+shade,196+shade,255))
        d.point((u,0),fill='#e8b477');d.point((u,height-1),fill='#e8b477')
    icon=emblem(index,height-3);flag.alpha_composite(icon,((width-icon.width)//2,1))
    for u in range(width):
        sway=round(math.sin(phase-u*.28)*u/12)
        im.alpha_composite(flag.crop((u,0,u+1,height)),(x+u,y+sway))

def banner(index,step):
    im=Image.new('RGBA',(96,112));d=ImageDraw.Draw(im)
    d.ellipse((42,82,57,88),fill=(32,27,20,65))
    d.line((48,84,48,31),fill='#4e3925',width=3);d.line((47,82,47,31),fill='#c7a570')
    d.polygon([(48,26),(51,31),(48,35),(45,31)],fill='#dfbd69',outline='#51412c')
    cloth(im,49,35,29,21,step*math.tau/12,index)
    return im

def bearer(index,facing,step,mode):
    from animation_art import unit_frame
    im=Image.new('RGBA',(88,80));moving=mode=='run';phase=step*math.tau/8
    im.alpha_composite(unit_frame('standardbearer',facing,step,mode),(16,28))
    d=ImageDraw.Draw(im);side=1 if math.sin(-facing*math.tau/8)>=0 else -1
    bob=round(abs(math.sin(phase))*1.1) if moving else round(math.sin(phase)*.55)
    x=44+side*6;tilt=round(math.sin(phase)*1.2) if moving else 0
    d.line((x,66+bob,x+tilt,12+bob),fill='#443320',width=3)
    d.line((x-1,64+bob,x+tilt-1,12+bob),fill='#d1b581')
    d.polygon([(x+tilt,7+bob),(x+tilt+3,12+bob),(x+tilt,15+bob),(x+tilt-3,12+bob)],fill='#e3c36f',outline='#604e2a')
    cloth(im,x+tilt+1,15+bob,27,20,phase,index)
    d.line((x-1,54+bob,x+1,54+bob),fill='#dbb78b',width=2)
    return im

def main(regenerate_existing=True):
    from animation_art import unit_frame,cart_frame,UNIT_MODES
    from medieval_art import ROLE_COLORS
    from decoration_art import frames as decoration_frames
    from caravan_art import main as caravan
    # Regenerate the procedural people only; painted building masters are untouched.
    if regenerate_existing:
        for name in ROLE_COLORS:
            frames=[cart_frame(f,s,m) if name=='supplycart' else unit_frame(name,f,s,m) for m in UNIT_MODES for f in range(8) for s in range(8)]
            save(name,frames)
        caravan()
        from building_animation_art import building_frames
        from isometric_art import construction
        from siege_art import frames as siege_frames,MODES,workshop_frames
        from trebuchet_art import save_sheet as save_trebuchet
        save('marketstall',building_frames('marketstall')+[construction('marketstall')])
        save('siegeworkshop',workshop_frames())
        for name in MODES:
            poses=siege_frames(name)
            if name=='trebuchet':save_trebuchet(MOD/'art/trebuchet.png',poses)
            else:save(name,poses)
    seq=[]
    for index in range(6):
        name=f'heraldic-banner-{index}';save(name,[banner(index,s) for s in range(12)]+[decoration_frames('banner')[-1]])
        seq += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png','\t\tOffset: 0, -28','\tidle:','\t\tLength: 12','\t\tTick: 100','\tconstruction:','\t\tStart: 12','']
        name=f'heraldic-standardbearer-{index}';save(name,[bearer(index,f,s,m) for m in ('stand','run') for f in range(8) for s in range(8)])
        seq += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png','\t\tOffset: 0, -26']
        for mode in UNIT_MODES:
            seq += [f'\t{mode}:',f'\t\tStart: {64 if mode in ("run","carry-run","push") else 0}','\t\tLength: 8','\t\tFacings: 8','\t\tTick: 120']
        seq += ['\tidle:','\t\tStart: 0','\t\tLength: 8','\t\tFacings: 8','']
    icons=[]
    for index in range(6):
        im=Image.new('RGBA',(64,48));cloth(im,8,8,48,31,0,index);icons.append(im)
    save('heraldry-icons',icons)
    seq += ['md.heraldry-icons:','\tDefaults:','\t\tFilename: medieval|art/heraldry-icons.png','\tidle:','\t\tLength: 6','']
    (MOD/'sequences/heraldry.yaml').write_text('\n'.join(seq),encoding='utf-8',newline='\n')
    preview=Image.new('RGBA',(6*96,200),'#465640')
    for i in range(6):preview.alpha_composite(banner(i,2),(96*i,0));preview.alpha_composite(bearer(i,6,2,'run'),(96*i,112))
    preview.save(MOD/'art/heraldry-preview.png')

if __name__=='__main__':main()
