"""Small, articulated wildlife and wild undergrowth in the mod's native pixel style."""
from pathlib import Path
import math
import random
from PIL import Image, ImageDraw, PngImagePlugin

MOD = Path(__file__).resolve().parents[1]
FRAMES = 12
VEGETATION = 12

def save(name, frames):
    w,h=frames[0].size; cols=min(16,len(frames))
    sheet=Image.new('RGBA',(w*cols,h*math.ceil(len(frames)/cols)))
    for i,im in enumerate(frames): sheet.alpha_composite(im,((i%cols)*w,(i//cols)*h))
    meta=PngImagePlugin.PngInfo();meta.add_text('FrameSize',f'{w},{h}');meta.add_text('FrameAmount',str(len(frames)))
    sheet.save(MOD/'art'/f'{name}.png',pnginfo=meta)

def animal(kind, facing, step, moving, fall=None):
    deer=kind=='deer';size=(80,80) if deer else (56,48);ground=64 if deer else 36
    im=Image.new('RGBA',size);d=ImageDraw.Draw(im);angle=facing*math.tau/8;phase=step*math.tau/FRAMES
    co,si=math.cos(angle),math.sin(angle)
    hop=max(0,math.sin(phase))*(2.1 if deer else 3.2) if moving else math.sin(phase)*.25
    def p(u,v,z=0):
        if fall is not None:
            t=fall*fall*(3-2*fall); tilt=t*math.pi*.47; pivot=12 if deer else 4
            v,z=v*math.cos(tilt)+(z-pivot)*math.sin(tilt),pivot+(z-pivot)*math.cos(tilt)-v*math.sin(tilt)-t*(pivot-3)
            u-=math.sin(t*math.pi)*2
        return (round(size[0]/2+u*co-v*si),round(ground+(u*si+v*co)*.52-z))
    def line(points,color,width=1):d.line([p(*a) for a in points],fill=color,width=width)
    def blob(at,rx,ry,color,outline=None):
        x,y=p(*at);d.ellipse((x-rx,y-ry,x+rx,y+ry),fill=color,outline=outline)
    def capsule(a,b,r,color):
        aa=p(*a);bb=p(*b);d.line([aa,bb],fill=color,width=2*r+1)
        for x,y in (aa,bb):d.ellipse((x-r,y-r,x+r,y+r),fill=color)
    ink='#493a29';fur='#98673f' if deer else '#918276';light='#c39665' if deer else '#b3a391'
    d.ellipse((size[0]//2-(12 if deer else 7),ground-3,size[0]//2+(12 if deer else 7),ground+3),fill=(21,29,18,55 if moving else 75))
    if deer:
        def leg(u,v):
            cycle=phase+(0 if u*v>0 else math.pi)
            stride=math.sin(cycle)*5 if moving else 0
            lift=max(0,math.cos(cycle))*5 if moving else 0
            knee=(u+stride*.3,v,7+lift*.3+hop*.4);foot=(u+stride,v,max(0,lift))
            line([(u,v,15+hop),knee,foot],ink,3)
            line([(u,v,14+hop),knee,(foot[0],v,foot[2]+1)],fur,2)
            line([foot,(foot[0]+2,v,foot[2])],'#302e26',2)
        legs=[(u,v) for u in (-7,6) for v in (-3,3)]
        legs.sort(key=lambda a:a[0]*si+a[1]*co)
        for u,v in legs[:2]:leg(u,v)
        capsule((-7,0,16+hop),(6,0,16+hop),5,ink)
        capsule((-7,0,17+hop),(6,0,17+hop),4,fur)
        line([(-7,-1,20+hop),(4,-1,20+hop)],light,2)
        line([(-6,0,12+hop),(3,0,12+hop)],'#d0ae82',2)
        blob((-8,0,16+hop),3,4,'#d8c6a3')
        line([(-10,0,17+hop),(-13,0,18+hop)],fur,3)
        for u,v in legs[2:]:leg(u,v)
        grazing=max(0,math.sin(phase-math.pi/2)) if not moving and fall is None else 0
        hz=26-19*grazing+hop
        hu=10+5*grazing
        capsule((6,0,17+hop),(hu,0,hz-2),3,ink)
        capsule((6,0,18+hop),(hu,0,hz-1),2,light)
        capsule((hu,0,hz),(hu+4,0,hz-1),2,fur)
        blob((hu+5,0,hz-1),1,1,'#322c23')
        for s in (-1,1):
            d.polygon([p(hu,0,hz+1),p(hu-3,s*5,hz+3),p(hu+1,s*3,hz+3)],fill=light,outline=ink)
            # The branched silhouette remains recognizable in all eight orientations.
            line([(hu-1,s*1.5,hz+2),(hu-3,s*3,hz+7),(hu-5,s*5,hz+12)],'#655039',2)
            line([(hu-3,s*3,hz+7),(hu+1,s*4,hz+10)],'#c6b794')
            line([(hu-4,s*4,hz+10),(hu-2,s*7,hz+13)],'#c6b794')
        ex,ey=p(hu+2,3 if co>=0 else -3,hz+1);d.point((ex,ey),fill='#171e18')
    else:
        for u in (-4,4):
            for v in (-2,2):
                cycle=phase+(0 if u>0 else 1.4)
                stride=math.sin(cycle)*3 if moving else 0
                foot=max(0,hop-1+max(0,math.cos(cycle))*2) if moving else 0
                line([(u,v,3+hop),(u+stride,v,foot)],ink,2)
                line([(u+stride,v,foot),(u+stride+2,v,foot)],light,2)
        capsule((-3,0,5+hop),(3,0,5+hop),3,ink)
        capsule((-3,0,6+hop),(3,0,6+hop),3,fur)
        line([(-4,-1,8+hop),(1,-1,8+hop)],light,2)
        blob((-6,0,6+hop),2,2,'#d4d0bc')
        hz=7+hop+(0 if moving else math.sin(phase)*.7)
        blob((5,0,hz),3,3,fur,ink)
        for v in (-1.5,1.5):
            tilt=-3+math.cos(phase)*2 if moving else math.sin(phase+v)*1.4
            line([(4,v,hz+2),(4+tilt,v*2,hz+9)],ink,3)
            line([(4,v,hz+3),(4+tilt,v*2,hz+8)],'#c1aa95')
        blob((7,0,hz-1),1,1,'#514438')
        ex,ey=p(5,2 if co>=0 else -2,hz+1);d.point((ex,ey),fill='#121b17')
    return im

def vegetation(variant, step):
    im=Image.new('RGBA',(64,48));d=ImageDraw.Draw(im);rng=random.Random(801+variant*47)
    sway=math.sin(step*math.tau/4+variant)*1.3
    # Moss, fern, tall grasses, daisies, heather and small woodland mushrooms.
    for i in range(22):
        x=round(32+rng.uniform(-18,18));y=round(36+rng.uniform(-5,5));h=rng.randint(3,13)
        tip=(round(x+sway*h/12+rng.uniform(-3,3)),y-h)
        color=rng.choice(('#536c32','#708441','#8c9651','#3e5b32'))
        if variant%6==0:  # Fern fronds.
            d.line([(x,y),tip],fill='#476338')
            for j in range(3,h,3):
                xx=round(x+(tip[0]-x)*j/h); yy=y-j;w=max(1,(h-j)//2)
                d.line([(xx-w,yy-2),(xx,yy),(xx+w,yy-2)],fill=color)
        elif variant%6==5:  # Fallen twigs, moss and toadstools.
            d.ellipse((x-2,y-1,x+2,y+1),fill=color)
            if i%5==0:
                d.line((x,y,x,y-4),fill='#c5b692');d.ellipse((x-3,y-6,x+3,y-3),fill='#ad7651',outline='#5e5035')
                d.point((x-1,y-5),fill='#e0cca1')
        else:
            d.line([(x,y),tip],fill=color)
            if variant%6 in (2,3,4) and i%2==0:
                flower=('#e2d5a6','#b99bbb','#a8bfa4')[variant%6-2]
                tx,ty=tip;d.line((tx-1,ty,tx+1,ty),fill=flower);d.line((tx,ty-1,tx,ty+1),fill=flower)
                d.point((tx,ty),fill='#cbb259')
            else:d.line([(x,y-2),(x+4,y-6)],fill='#8c9a54')
    return im

def main():
    seq=[];contact=Image.new('RGB',(720,360),'#657447');d=ImageDraw.Draw(contact)
    for kind in ('rabbit','deer'):
        frames=[animal(kind,f,s,m) for m in (False,True) for f in range(8) for s in range(FRAMES)]
        save('wild-'+kind,frames)
        offset=-24 if kind=='deer' else -12
        seq += [f'md.wild-{kind}:','\tidle:',f'\t\tFilename: medieval|art/wild-{kind}.png',f'\t\tOffset: 0, {offset}','\t\tLength: 192','']
        for f in range(8):
            x=10+f*88;y=90 if kind=='deer' else 14
            contact.paste(frames[96+f*12+2],(x,y),frames[96+f*12+2])
    plants=[vegetation(v,s) for v in range(VEGETATION) for s in range(4)]
    save('wild-undergrowth',plants)
    seq += ['md.wild-undergrowth:','\tidle:','\t\tFilename: medieval|art/wild-undergrowth.png','\t\tOffset: 0, -12','\t\tLength: 48','']
    for v in range(VEGETATION):
        frame=plants[v*4];contact.paste(frame,(12+(v%8)*88,210+(v//8)*65),frame)
    (MOD/'sequences/wilderness.yaml').write_text('\n'.join(seq),encoding='utf-8',newline='\n')
    out=MOD.parents[1]/'analysis/medieval/wilderness';out.mkdir(parents=True,exist_ok=True)
    contact.save(out/'sprites.png')
    for name,frames in [('rabbit',[animal('rabbit',0,s,True) for s in range(12)]),('deer',[animal('deer',0,s,True) for s in range(12)])]:
        preview=[]
        for frame in frames:
            bg=Image.new('RGBA',frame.size,'#657447');bg.alpha_composite(frame);preview.append(bg.resize((frame.width*4,frame.height*4),Image.Resampling.NEAREST).convert('RGB'))
        preview[0].save(out/f'{name}.gif',save_all=True,append_images=preview[1:],duration=80,loop=0)

if __name__=='__main__':main()
