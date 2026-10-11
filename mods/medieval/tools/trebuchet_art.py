"""Articulated trebuchet: rigid timbers, parked wagon and a continuous reversible assembly.

The same model provides transport, deployment and firing endpoints in eight facings.
All coordinates are native editable artwork, independent of the painted buildings.
"""
import math
from PIL import Image, ImageDraw, PngImagePlugin
from pathlib import Path
from animation_art import unit_frame

FRAME = (256, 256)
OFFSET = (0, -72)
ASSEMBLY_FRAMES = 48
LENGTHS = {'idle': 1, 'run': 8, 'assemble': ASSEMBLY_FRAMES, 'deployed': 1, 'fire': 32}

def shot_smooth(t,a,b):
    # Integer curve shared with SiegeShotMotion; quantized poses match the real sling.
    u=max(0,min(1000,int((t-a)*1000/(b-a))))
    return u*u*(3000-2*u)//1000000

def shot_pose(step):
    t=step*1000//(LENGTHS['fire']-1)
    angle=(-49+219*shot_smooth(t,0,190)//1000 if t<190 else
           170-20*shot_smooth(t,190,320)//1000 if t<320 else
           150-199*shot_smooth(t,320,1000)//1000)
    lift=-12+18*(shot_smooth(t,0,190) if t<320 else 1000-shot_smooth(t,320,800))//1000
    return angle*math.tau/1024,lift

def ease(value, start, end):
    t=max(0,min(1,(value-start)/(end-start)))
    return t*t*(3-2*t)

def mix(a,b,t): return tuple(x+(y-x)*t for x,y in zip(a,b))

def rigid(a,b,c,d,t,lift=0):
    """Move/rotate one timber without stretching it during interpolation."""
    center=mix(mix(a,b,.5),mix(c,d,.5),t)
    delta=mix(tuple(y-x for x,y in zip(a,b)),tuple(y-x for x,y in zip(c,d)),t)
    length=math.sqrt(sum((y-x)**2 for x,y in zip(a,b)))
    norm=math.sqrt(sum(v*v for v in delta)) or 1
    half=tuple(v/norm*length/2 for v in delta)
    center=(center[0],center[1],center[2]+math.sin(math.pi*t)*lift)
    return tuple(x-y for x,y in zip(center,half)),tuple(x+y for x,y in zip(center,half))

class Scene:
    def __init__(self,facing):
        self.scale=2
        angle=-facing*math.tau/8
        self.dx,self.dy=math.sin(angle),-math.cos(angle)
        self.items=[]
    def p(self,a):
        f,s,z=a
        return (round((128+self.dx*f-self.dy*s)*self.scale),round((200+(self.dy*f+self.dx*s)*.5-z)*self.scale))
    def depth(self,points): return sum(self.dy*q[0]+self.dx*q[1] for q in points)/len(points)
    def add(self,points,draw,bias=0): self.items.append((self.depth(points)+bias,draw))
    def line(self,points,color,width=1,bias=0):
        self.add(points,lambda im,d:d.line([self.p(a) for a in points],fill=color,width=max(1,round(width*self.scale))),bias)
    def beam(self,a,b,width=5):
        def draw(im,d):
            aa,bb=self.p(a),self.p(b)
            d.line([aa,bb],fill='#352c22',width=round((width+1.5)*self.scale))
            d.line([aa,bb],fill='#987044',width=round(width*self.scale))
            d.line([(aa[0]-1,aa[1]-self.scale),(bb[0]-1,bb[1]-self.scale)],fill='#d0ac74',width=max(1,round(width*self.scale/4)))
            for pt in (aa,bb):
                r=1.1*self.scale;d.ellipse((pt[0]-r,pt[1]-r,pt[0]+r,pt[1]+r),fill='#454a40',outline='#b6ab89')
        self.add([a,b],draw)
    def poly(self,points,fill,outline='#413323',bias=0):
        self.add(points,lambda im,d:d.polygon([self.p(a) for a in points],fill=fill,outline=outline,width=self.scale),bias)
    def box(self,f,s,z,l,w,h):
        for points,col in [([(f,s,z),(f+l,s,z),(f+l,s,z+h),(f,s,z+h)],'#795631'),
            ([(f,s+w,z),(f+l,s+w,z),(f+l,s+w,z+h),(f,s+w,z+h)],'#a67d4b'),
            ([(f,s,z),(f,s+w,z),(f,s+w,z+h),(f,s,z+h)],'#6e5031'),
            ([(f+l,s,z),(f+l,s+w,z),(f+l,s+w,z+h),(f+l,s,z+h)],'#977044')]: self.poly(points,col)
        self.poly([(f,s,z+h),(f+l,s,z+h),(f+l,s+w,z+h),(f,s+w,z+h)],'#c6a476',bias=.1)
    def wheel(self,f,s,z,angle):
        def draw(im,d):
            x,y=self.p((f,s,z));rx=(3.8+abs(self.dx)*2)*self.scale;ry=8*self.scale
            d.ellipse((x-rx,y-ry,x+rx,y+ry),fill='#65482e',outline='#312b22',width=self.scale)
            d.ellipse((x-rx+2,y-ry+2,x+rx-2,y+ry-2),outline='#c09b60',width=self.scale)
            for i in range(4):
                a=angle+i*math.pi/2
                d.line((x,y,x+math.cos(a)*(rx-2),y+math.sin(a)*(ry-2)),fill='#c09b60',width=self.scale)
            mx=x+math.cos(angle+.25)*rx*.73;my=y+math.sin(angle+.25)*ry*.77
            r=1.05*self.scale;d.ellipse((mx-r,my-r,mx+r,my+r),fill='#ddd0a5',outline='#4a4939')
            r=2*self.scale;d.ellipse((x-r,y-r,x+r,y+r),fill='#697065',outline='#302f27')
        self.add([(f,s,z)],draw)
    def human(self,f,s,facing,step,mode):
        def draw(im,d):
            sprite=unit_frame('builder',facing,step%8,mode)
            sprite=sprite.resize((sprite.width*self.scale,sprite.height*self.scale),Image.Resampling.NEAREST)
            x,y=self.p((f,s,0));im.alpha_composite(sprite,(x-28*self.scale,y-38*self.scale))
        self.add([(f,s,0)],draw)
    def render(self):
        im=Image.new('RGBA',(FRAME[0]*self.scale,FRAME[1]*self.scale));d=ImageDraw.Draw(im)
        for _,draw in sorted(self.items,key=lambda pair:pair[0]): draw(im,d)
        return im.resize(FRAME,Image.Resampling.LANCZOS)

def frame(facing,step,mode):
    scene=Scene(facing)
    build=step/(ASSEMBLY_FRAMES-1) if mode=='assemble' else 1 if mode in ('deployed','fire') else 0
    moving=mode=='run'
    rolling=ease(build,.02,.18)
    cartf,carts=-32*rolling,-32*rolling
    def load(f,s,z): return (f+cartf,s+carts,z)
    scene.poly([load(-31,-20,0),load(29,-20,0),load(29,20,0),load(-31,20,0)],(18,25,18,48),None,bias=-200)
    if build>0:
        scene.poly([(-38,-29,0),(38,-29,0),(38,29,0),(-38,29,0)],(18,25,18,round(48*ease(build,.1,.4))),None,bias=-200)
    # The transport wagon remains beside the deployed machine; no disappearance between states.
    wheel_phase=step*math.tau/8 if moving else rolling*math.pi*3
    for s in (-15,15):
        for f in (-16,16): scene.wheel(f+cartf,s+carts,8,wheel_phase)
        scene.beam(load(-27,s,13),load(27,s,13),4)
        scene.beam(load(-27,s,13),load(-53,s,13),3)
    for f in range(-25,26,5): scene.box(f+cartf,-13+carts,13,4.5,26,3)

    # Rails and cross members are lifted off in order. All beams retain their lengths.
    for i,s in enumerate((-24,24)):
        t=ease(build,.13+i*.025,.29+i*.025)
        a,b=rigid(load(-34,-6+i*12,22),load(34,-6+i*12,22),(-34,s,4),(34,s,4),t,10)
        scene.beam(a,b,6)
    for i,f in enumerate((-28,0,28)):
        t=ease(build,.22+i*.025,.34+i*.025)
        a,b=rigid(load(-29,-8+i*8,28),load(29,-8+i*8,28),(f,-29,8),(f,29,8),t,11)
        scene.beam(a,b,4)

    # The two A-frames hinge up as rigid triangles; the feet are fixed after unloading.
    for i,s in enumerate((-18,18)):
        unload=ease(build,.28+i*.035,.42+i*.035)
        angle=ease(build,.43+i*.065,.64+i*.065)*math.pi/2
        outward=-1 if s<0 else 1
        apex=(0,s+outward*57*math.cos(angle),8+57*math.sin(angle))
        for j,f in enumerate((-27,27)):
            length=math.sqrt(27**2+57**2)
            a,b=rigid(load(-length/2,-7+i*9+j*3,34+j*4),load(length/2,-7+i*9+j*3,34+j*4),(f,s,8),apex,unload,7)
            scene.beam(a,b,5)
        brace_s=s+outward*19*math.cos(angle);brace_z=8+19*math.sin(angle)
        a,b=rigid(load(-18,-8+i*12,42),load(18,-8+i*12,42),(-18,brace_s,brace_z),(18,brace_s,brace_z),unload,7)
        scene.beam(a,b,3)
        if .42<build<.73:
            scene.line([(-22,22,17),apex], '#c8af7f',1.1,bias=2)
    # Axle, arm and counterweight are installed separately, after the supports are upright.
    t=ease(build,.68,.77)
    a,b=rigid(load(-23,2,46),load(23,2,46),(0,-23,65),(0,23,65),t,8)
    scene.beam(a,b,6)
    fire_angle=-49*math.tau/1024
    if mode=='fire':
        # Quick release, brief recoil, then a slower winch return.
        fire_angle,_=shot_pose(step)
    def arm(t): return (math.cos(fire_angle)*t,0,65+math.sin(fire_angle)*t)
    arm_t=ease(build,.75,.88)
    a,b=rigid(load(-35,0,50),load(51,0,50),arm(-23),arm(63),arm_t,10)
    scene.beam(a,b,5)
    weight_t=ease(build,.865,.96)
    cf,cs,cz=arm(-21)
    weight=mix(load(-12,-10,27),(cf-7,-10,cz-23),weight_t)
    scene.box(*weight,14,20,22)
    for s in (0,20):
        scene.line([(weight[0],weight[1]+s,weight[2]+6),(weight[0]+14,weight[1]+s,weight[2]+6)],'#555d51',2)
    scene.poly([(weight[0],weight[1],weight[2]+22),(weight[0]+14,weight[1],weight[2]+22),
        (weight[0]+14,weight[1]+20,weight[2]+22),(weight[0],weight[1]+20,weight[2]+22)],'#607b86')
    if weight_t>0 and weight_t<1:
        scene.line([arm(-21),(weight[0]+7,weight[1]+10,weight[2]+22)],'#d4b984',1.1)
    # Loading straps loosen before unloading and are tied last during packing.
    if build<.13:
        slack=ease(build,.015,.13)*11
        for f in (-14,16): scene.line([load(f,-14,18),load(f,-14-slack,54),load(f,14+slack,54),load(f,14,18)],'#c8af7c',1.3)
    # Sling unfurls only after the beam is secured. Rope stays attached while firing.
    rig=ease(build,.91,1)
    if rig>0:
        end=arm(63)
        lift=shot_pose(step)[1] if mode=='fire' else -12
        tip=(end[0]+14*rig,0,end[2]+lift*rig)
        scene.line([end,tip],'#d8c393',1.1,bias=1)
        f,s,z=tip
        scene.poly([(f-3,s-2,z),(f+4,s-2,z),(f+3,s+3,z),(f-3,s+3,z)],'#7b5938',bias=1)
    # Windlass is placed during assembly and used while raising the two supports.
    crank_t=ease(build,.36,.45)
    a,b=rigid(load(-21,-3,19),load(21,-3,19),(-22,-21,17),(-22,21,17),crank_t,4)
    scene.beam(a,b,4)
    if crank_t>.99:
        turn=step*.78 if mode=='assemble' and 0<build<1 else step/(LENGTHS['fire']-1)*math.tau*2 if mode=='fire' else 0
        scene.line([(-22,22,17),(-22+math.sin(turn)*8,22,17+math.cos(turn)*8)],'#c6ad7c',2)
        for f in (-25,-23,-21,-19): scene.line([(f,-5,19),(f,5,19)],'#cab385',1)
    # Exactly one visible operator walks between the wagon, the supports and the windlass.
    route=[(0,(-59,0)),(.16,(-66,-32)),(.36,(-42,18)),(.52,(-24,30)),(.88,(-23,30)),(1,(-40,18))]
    pos=route[-1][1]
    for (ta,a),(tb,b) in zip(route,route[1:]):
        if ta<=build<=tb: pos=mix(a,b,ease(build,ta,tb));break
    df,ds=-pos[0],-pos[1]
    wx,wy=scene.dx*df-scene.dy*ds,scene.dy*df+scene.dx*ds
    human_facing=round(math.atan2(-wx,-wy)/math.tau*8)%8 if build>0 else facing
    human_mode='push' if moving else 'push-stand' if build==0 else 'stand' if build==1 else 'carry-run' if .18<build<.36 else 'work' if .40<build<.9 else 'run'
    scene.human(*pos,human_facing,step if build not in (0,1) or moving else 0,human_mode)
    return scene.render()

def save_sheet(path, poses):
    """Pack visible pixels only; PNG frame offsets retain the exact ground anchor."""
    placements=[];x=y=row_height=0;width=2048
    for pose in poses:
        left,top,right,bottom=pose.getbbox()
        bounds=(max(0,left-2),max(0,top-2),min(FRAME[0],right+2),min(FRAME[1],bottom+2))
        crop=pose.crop(bounds)
        if x+crop.width>width: x=0;y+=row_height;row_height=0
        placements.append((crop,x,y,bounds))
        x+=crop.width;row_height=max(row_height,crop.height)
    sheet=Image.new('RGBA',(width,y+row_height))
    meta=PngImagePlugin.PngInfo()
    meta.add_text('FrameSize',','.join(map(str,FRAME)))
    meta.add_text('FrameAmount',str(len(poses)))
    for i,(crop,x,y,bounds) in enumerate(placements):
        sheet.paste(crop,(x,y))
        dx=bounds[0]+crop.width/2-FRAME[0]/2
        dy=bounds[1]+crop.height/2-FRAME[1]/2
        meta.add_text(f'Frame[{i}]',f'{x},{y},{crop.width},{crop.height};{dx:g},{dy:g}')
    sheet.save(path,pnginfo=meta)

if __name__=='__main__':
    # Standalone compiler/preview for this editable native asset.
    mod=Path(__file__).resolve().parents[1]
    poses=[frame(facing,step,mode) for mode,length in LENGTHS.items() for facing in range(8) for step in range(length)]
    save_sheet(mod/'art/trebuchet.png',poses)
    output=mod.parents[1]/'analysis/medieval';output.mkdir(parents=True,exist_ok=True)
    contact=Image.new('RGBA',(256*8,272*2),'#53613b');d=ImageDraw.Draw(contact)
    for row,facing in enumerate((1,6)):
        for col,step in enumerate((0,6,13,20,27,34,41,47)):
            contact.alpha_composite(frame(facing,step,'assemble'),(col*256,row*272))
            d.text((col*256+10,row*272+251),f'{round(step/47*100)}%',fill='#eee1b6')
    contact.convert('RGB').save(output/'trebuchet-stages.png')
    frames=[]
    for step in list(range(48))+list(reversed(range(48))):
        im=Image.new('RGBA',FRAME,'#53613b');im.alpha_composite(frame(1,step,'assemble'));frames.append(im.convert('RGB'))
    frames[0].save(output/'trebuchet-montage.gif',save_all=True,append_images=frames[1:],duration=[250]*48+[167]*48,loop=0,disposal=2)
    print(f'Trebuchet: {len(poses)} native poses; stages and reversible motion preview exported.')
