"""Native pixel animation: jointed poses, fixed ground anchors, eight facings.

Every frame is drawn from the pose; limbs are not a sliding copy of the body.
"""
import math
from pathlib import Path
from PIL import Image, ImageDraw
from medieval_art import ROLE_COLORS, INK

UNIT_FRAMES = 8
UNIT_MODES = ('stand', 'run', 'work', 'harvest', 'carry-stand', 'carry-run', 'load', 'attack', 'push', 'push-stand')
ZONE_FRAMES = 8
MILL_FRAMES = 16


def farm_frames():
    # Generated stationary building + code-native rotor: only the mechanism moves.
    base=Image.open(Path(__file__).resolve().parents[1]/'art-source/farm-animation-base.png').convert('RGBA').resize((184,184),Image.Resampling.LANCZOS)
    result=[]
    for frame in range(MILL_FRAMES):
        im=base.copy();d=ImageDraw.Draw(im)
        def rotor(u,v): return (round(64+u*.78),round(82+v+u*.18))
        for arm in range(4):
            angle=math.pi/4+arm*math.pi/2+frame*math.pi/(2*MILL_FRAMES)
            def p(r,w=0): return rotor(math.cos(angle)*r-math.sin(angle)*w,math.sin(angle)*r+math.cos(angle)*w)
            d.line([p(0),p(42)],fill='#493820',width=3)
            d.line([p(2),p(41)],fill='#b99961')
            d.polygon([p(13,1),p(42,1),p(42,7),p(13,7)],fill='#987545',outline='#423621')
            d.line([p(14,6),p(41,6)],fill='#ddbd7b')
            for r in range(15,42,4):
                d.line([p(r,1),p(r,7)],fill='#d1b579')
                d.line([p(r+1,2),p(r+1,6)],fill='#61492c')
        d.ellipse((61,79,67,85),fill='#947345',outline='#3f3627')
        d.point((63,80),fill='#dfc18b')
        result.append(im)
    return result


def unit_frame(name, facing, step, mode='stand'):
    im = Image.new('RGBA', (56, 52)); d = ImageDraw.Draw(im)
    # OpenRA yaw is counterclockwise from north (east is facing 6).
    angle = -facing * math.tau / 8
    dx, dy = math.sin(angle), -math.cos(angle)
    side = 1 if dx >= 0 else -1
    rear, profile = dy < -.25, abs(dx) > .9
    phase = step * math.tau / UNIT_FRAMES
    moving = mode in ('run', 'carry-run', 'push')
    carrying = mode.startswith('carry')
    working = mode in ('work', 'harvest', 'attack')
    bowman = name in ('archer', 'elitearcher')
    armored = name in ('manatarms', 'royalguard', 'commander')
    # Anticipation, raised tool, accelerating strike, contact, follow-through.
    strike = (0, -.25, -.55, -.8, -.15, 1, .65, .2)[step]
    bend = max(0, strike) * (3 if name in ('farmer', 'breeder') else 1.5) if working else 0
    if mode == 'load': bend = (0, 1, 3, 4, 4, 2, 1, 0)[step]
    bob = round(abs(math.sin(phase)) * 1.1) if moving else round(math.sin(phase) * .55)
    if working: bob = round(bend)
    x, y = 28 + round(dx * bend * .45), 31 + bob
    # Reserved magenta hue is shifted to the owner's empire color by the GPU.
    # Skin, tools, leather and bare steel keep their native material colors.
    shirt = '#c44fc4'
    def line(a, b, color, width=1): d.line((*a, *b), fill=color, width=width)
    def point(a, b): return (round(a), round(b))
    d.ellipse((20, 36, 38, 41), fill=(17, 24, 18, 85))
    if name=='messenger':
        sway=round(math.sin(phase)*2) if moving else 0
        d.polygon([(x-4,21+bob),(x+4,21+bob),(x+5+sway,34+bob),(x-5+sway,34+bob)],fill='#634577',outline=INK)
        line((x-3,23+bob),(x-4+sway,32+bob),'#aa88bb')
    if name in ('royalguard','commander'):
        sway=round(math.sin(phase)*2) if moving else 0
        d.polygon([(x-4,21+bob),(x+4,21+bob),(x+6+sway,36+bob),(x-5+sway,35+bob)],fill='#953d95' if name=='commander' else '#284964',outline='#293b43')
        line((x-3,24+bob),(x-4+sway,34+bob),'#e0c281' if name=='commander' else '#7091a0')
    if name=='elitearcher':
        qx=x-side*4
        line((qx,30+bob),(qx-side*3,20+bob),'#69503b',4)
        for fletch in (-1,1,3):
            line((qx-side*2+fletch,24+bob),(qx-side*4+fletch,15+bob),'#b3a27b')
            line((qx-side*4+fletch,15+bob),(qx-side*4+fletch+2,17+bob),'#d2d5bd',2)
    # Feet alternate planted contact and lifted recovery; knees articulate.
    for s in sorted((-1, 1), key=lambda s: s * dx):
        stride = math.sin(phase + (math.pi if s < 0 else 0)) * 3.2 if moving else s * .4
        lift = max(0, math.cos(phase + (math.pi if s < 0 else 0))) * 1.7 if moving else 0
        hip = (x + s * 2, 30 + bob)
        foot = point(28 + s * 2 + dx * stride, 38 + dy * stride * .5 - lift)
        knee = point((hip[0] + foot[0]) / 2 + dx * lift, 34 + bob * .3)
        d.line([hip, knee, foot], fill='#343c35', width=3)
        line((knee[0]-1,knee[1]), (foot[0]-1,foot[1]-1), '#786a52')
        line((foot[0]-1,foot[1]), (foot[0]+2,foot[1]), '#272b25', 2)
    half = 3 if profile else 4
    d.polygon([(x-half,21+bob),(x+half,21+bob),(x+half+1,31+bob),(x-half-1,31+bob)], fill=shirt, outline=INK)
    line((x-half+1,23+bob),(x-half+1,28+bob),'#c8b184' if name not in ('manatarms','lumberjack') else '#a6b6a8',2)
    line((x+half,23+bob),(x+half,30+bob),'#555341')
    line((x-half,29+bob),(x+half,29+bob),'#473c2d',2)
    d.point((x,29+bob),fill='#c8ad67')
    line((x-2,22+bob),(x+1,23+bob),'#5d89a0',2)
    # Both hands follow the tool shaft for work and brace the load in transit.
    arms=[]
    for s in (-1,1):
        shoulder=(x+s*(half+1),23+bob)
        if name=='standardbearer':
            hand=point(x+side*6,27+bob)
        elif name=='drummer' and not carrying and mode!='load':
            beat=math.sin(phase+(math.pi if s<0 else 0)) if working or moving else 0
            hand=point(x+s*4+dx*2,26+bob+dy*2+beat*3)
        elif bowman and not carrying and mode!='load':
            pull=(2+step*.65) if working else 3
            hand=point(x+dx*(9 if s<0 else 9-pull)+s,24+bob+dy*(5 if s<0 else 5-pull*.5))
        elif working:
            hand=point(x+dx*(6+strike*3)+s*1.5, 25+bob+dy*2+strike*5)
        elif carrying or mode == 'load':
            hand=point(x+s*4+dx*2,29+bob+dy*2)
        elif mode in ('push','push-stand'):
            hand=point(x+s*4+dx*8,26+bob+dy*4)
        else:
            swing=math.sin(phase)*s*2 if moving else 0
            hand=point(x+s*(half+2)-dx*swing,29+bob-dy*swing)
        elbow=point((shoulder[0]+hand[0])/2+s, (shoulder[1]+hand[1])/2+1)
        d.line([shoulder,elbow,hand],fill=INK,width=3)
        d.line([shoulder,elbow,(hand[0],hand[1]-1)],fill=shirt,width=2)
        d.point(hand,fill='#d0ae7e'); arms.append(hand)
    torso_x, torso_bob = x, bob
    x += round(dx*1.5); bob += round(dy*.75)
    d.rectangle((x-1,19+bob,x+1,22+bob),fill='#ac815a')
    d.ellipse((x-3,13+bob,x+3,20+bob),fill='#98714b' if rear else '#cda775',outline=INK)
    line((x-2,14+bob),(x+1,14+bob),'#674f35',2)
    if rear: d.rectangle((x-2,15+bob,x+2,18+bob),fill='#6b5136')
    else:
        d.point((x+side*2,17+bob),fill='#343a2b')
        d.point((x+side*3,18+bob),fill='#e0bc85')
    if name in ('farmer','breeder'):
        d.ellipse((x-6,14+bob,x+6,17+bob),fill='#bb9d5b',outline='#5a4e31')
        d.rectangle((x-3,12+bob,x+2,15+bob),fill='#c5ab69',outline='#746038')
    if name=='stonecutter': line((x-3,14+bob),(x+3,14+bob),'#a3b0ac',3)
    if name=='drummer':
        d.polygon([(x-4,14+bob),(x-2,11+bob),(x+3,12+bob),(x+4,15+bob)],fill='#527893',outline='#2c424a')
        line((x+2,12+bob),(x+4,8+bob),'#d8c28b',2)
        line((torso_x-3,21+torso_bob),(torso_x+4,30+torso_bob),'#dfc893',2)
    if name=='messenger':
        d.arc((x-4,10+bob,x+4,21+bob),175,365,fill='#705087',width=3)
        line((torso_x-3,21+torso_bob),(torso_x+4,30+torso_bob),'#d6b981',2)
    if name=='blacksmith':
        d.rectangle((torso_x-3,24+torso_bob,torso_x+3,32+torso_bob),fill='#65432f',outline='#3a2a22')
        d.rectangle((x-2,19+bob,x+2,21+bob),fill='#4a3729')
    if name=='swordsman':
        d.arc((x-4,10+bob,x+4,18+bob),180,355,fill='#a6b7b8',width=3)
    if name=='elitearcher':
        d.arc((x-4,10+bob,x+4,21+bob),175,365,fill='#294a46',width=3)
        line((x+side*3,13+bob),(x+side*5,9+bob),'#d4cc9b',2)
        for s in (-1,1):
            line((torso_x+s*4,22+torso_bob),(torso_x+s*5,25+torso_bob),'#adb9ae',2)
    if armored:
        d.polygon([(x-4,14+bob),(x-2,11+bob),(x+2,11+bob),(x+4,15+bob),(x+3,18+bob),(x-3,18+bob)],fill='#7d9098',outline='#313c3d')
        line((x-2,12+bob),(x+1,12+bob),'#e0e1ce')
        line((x-3,16+bob),(x+3,16+bob),'#293334')
        line((x,13+bob),(x,18+bob),'#bdc9c3')
        if name in ('royalguard','commander'):
            line((x-3,14+bob),(x+3,14+bob),'#d1b66c')
            plume=round(math.sin(phase)*1.5) if moving else 0
            d.polygon([(x,12+bob),(x-1,7+bob),(x+3+plume,6+bob),(x+4+plume,9+bob),(x+1,10+bob)],fill='#e5d3a4' if name=='commander' else '#3e708e',outline='#293d4c')
            d.polygon([(torso_x-3,23+torso_bob),(torso_x+3,23+torso_bob),(torso_x+3,29+torso_bob),(torso_x,31+torso_bob),(torso_x-3,29+torso_bob)],fill='#9bafb5',outline='#465b66')
            line((torso_x,24+torso_bob),(torso_x,30+torso_bob),'#e6d5a0')
    x, bob = torso_x, torso_bob
    hand=arms[1 if side>0 else 0]; hx,hy=hand
    if carrying or mode == 'load':
        # The resource itself is overlaid from the real inventory in the engine.
        bx,by=point(x+dx*2,30+bob+dy*2)
        d.polygon([(bx-5,by-4),(bx+5,by-4),(bx+4,by+3),(bx-4,by+3)],fill='#957448',outline='#433c2b')
        for yy in (-2,1): line((bx-4,by+yy),(bx+4,by+yy),'#c0a270')
    elif name=='messenger':
        d.rectangle((x+2,28+bob,x+7,33+bob),fill='#a47a45',outline='#463824')
        line((x+3,29+bob),(x+6,29+bob),'#e0c494')
        d.point((x+4,31+bob),fill='#d8bd75')
    elif name=='drummer':
        bx,by=point(x+dx*4,31+bob+dy*3)
        d.rectangle((bx-6,by-2,bx+6,by+5),fill='#9b5938',outline='#403528')
        for cord in (-4,0,4):
            line((bx+cord-1,by-2),(bx+cord+1,by+5),'#e1c987')
        d.ellipse((bx-6,by-5,bx+6,by+1),fill='#d6c18b',outline='#7b6138')
        line((bx-5,by+5),(bx+5,by+5),'#cfb16b',2)
        for s,hand in zip((-1,1),arms):
            beat=math.sin(phase+(math.pi if s<0 else 0)) if working or moving else 0
            tip=point(bx+s*2,by-4-max(0,beat)*6)
            line(hand,tip,'#dfc89c',2);d.point(tip,fill='#f0dab5')
    elif bowman and mode not in ('push','push-stand'):
        bx,by=point(x+dx*9,24+bob+dy*5)
        radius=9 if name=='elitearcher' else 7
        d.line([(bx-side*2,by-radius),(bx+side*3,by-4),(bx+side*4,by),(bx+side*3,by+4),(bx-side*2,by+radius)],fill='#d0b47b' if name=='elitearcher' else '#c5a071',width=2)
        pull=round(step*.5) if working else 0
        d.line([(bx-side*2,by-radius),(bx-side*(2+pull),by),(bx-side*2,by+radius)],fill='#d7d4b6')
        if working:line((bx-side*(2+pull),by),(bx+side*9,by),'#c8caba')
    elif name=='recruiter':
        d.rectangle((hx-2,hy-5,hx+3,hy+2),fill='#cfbd8b',outline='#65573c')
        for yline in (-3,-1):line((hx-1,hy+yline),(hx+2,hy+yline),'#7f7659')
    elif mode not in ('push','push-stand') and name not in ('hauler','standardbearer'):
        if working:
            # Tools lift above the shoulder then strike in the actor's facing.
            reach=9+strike*5
            tx,ty=point(hx+dx*reach, hy+dy*reach*.5-10+strike*12)
        else: tx,ty=point(hx+side*2,hy-13)
        tx=max(8,min(47,tx)); ty=max(8,min(43,ty))
        line((hx,hy),(tx,ty),'#423b2b',3)
        line((hx,hy),(tx,ty),'#c2a477',1)
        ax,ay=tx-hx,ty-hy
        length=max(1,math.hypot(ax,ay)); nx,ny=-ay/length,ax/length
        def tip(a,b=0): return point(tx+nx*a+ax/length*b,ty+ny*a+ay/length*b)
        if name in ('builder','stonecutter','blacksmith') or name=='commander' and mode=='work':
            if name in ('builder','blacksmith','commander'):
                d.polygon([tip(-3,-2),tip(3,-2),tip(3,1),tip(-3,1)],fill='#869089',outline='#323c36')
                line(tip(-2,-2),tip(2,-2),'#d3d6bc')
            else: d.line([tip(-6,-1),tip(-2,1),tip(2,1),tip(6,-1)],fill='#bac3b4',width=2)
        elif name=='lumberjack':
            d.polygon([tip(0,-2),tip(5,-3),tip(6,2),tip(0,1)],fill='#939e96',outline='#3d4840')
            line(tip(5,-2),tip(6,1),'#d4ddca')
        elif name=='farmer':
            if mode=='harvest':
                d.line([tip(-3,0),tip(-4,3),tip(-1,5),tip(3,4)],fill='#cbd3ba',width=2)
            else:
                line(tip(-3,0),tip(3,0),'#a8aca0',2)
                for a in (-3,0,3): line(tip(a,0),tip(a,4),'#c3c9b1')
        elif name=='breeder':
            if working:
                d.ellipse((tx-3,ty-1,tx+3,ty+3),fill='#b99f62',outline='#554833')
            else: d.arc((tx-3,ty-4,tx+2,ty+2),180,340,fill='#be9a5d',width=2)
        elif name in ('militia','manatarms','swordsman','royalguard','commander'):
            line((hx,hy),(tx,ty),'#aa8b52' if name=='militia' else '#cfddd4',2)
            if name=='militia': d.ellipse((tx-2,ty-2,tx+2,ty+2),fill='#8e7650',outline='#33372d')
            else: line((hx-2,hy-1),(hx+2,hy+1),'#c4a970')
        if working and step in (5,6) and (name not in ('militia','manatarms','swordsman','royalguard','commander') or name=='commander' and mode=='work'):
            color='#c5c4ac' if name=='stonecutter' else '#ba9560' if name in ('builder','lumberjack') else '#bda461'
            for i in range(3):
                px,py=point(tx+(i-1)*(3+step-5),ty-1-i*2-(step-5)*2)
                if 2<px<53 and 2<py<49: line((px,py),(px+1,py),color)
    if name=='hauler' and not carrying and mode not in ('load','push','push-stand'):
        bx=x-4 if rear else x+4
        d.polygon([(bx,21+bob),(bx+6,22+bob),(bx+6,31+bob),(bx,30+bob)],fill='#8b693e',outline='#433c2b')
        for yy in (24,27): line((bx+1,yy+bob),(bx+5,yy+1+bob),'#c0a270')
    if name in ('militia','manatarms','swordsman','royalguard') or name=='commander' and not carrying and mode not in ('work','load'):
        sx=x-side*6
        wide=4 if name=='royalguard' else 3
        d.polygon([(sx-wide,23+bob),(sx+wide,23+bob),(sx+wide,29+bob),(sx,(34 if name=='royalguard' else 33)+bob),(sx-wide,29+bob)],fill='#355c7b' if name=='royalguard' else '#49748d' if name=='manatarms' else '#8f703f',outline='#bba15a' if name=='royalguard' else '#25352e')
        line((sx-2,24+bob),(sx-2,29+bob),'#b2beb0')
        line((sx,25+bob),(sx,29+bob),'#cfb870')
        if name=='royalguard': line((sx-2,26+bob),(sx+2,26+bob),'#dfc879')
    if armored:
        # A cloth tabard and shield stripe carry the livery over the steel armor.
        d.polygon([(x-2,23+bob),(x+2,23+bob),(x+2,29+bob),(x,32+bob),(x-2,29+bob)],fill=shirt,outline='#673c67')
        line((x-1,24+bob),(x-1,29+bob),'#e69de6')
        if name!='commander' or not carrying and mode not in ('work','load'):
            line((sx,24+bob),(sx,30+bob),shirt,2)
    return im


def cart_frame(facing,step,mode='stand'):
    im=Image.new('RGBA',(72,80)); d=ImageDraw.Draw(im)
    angle=-facing*math.tau/8
    moving=mode in ('run','carry-run')
    phase=step*math.tau/8 if moving else 0
    bounce=round(math.sin(phase*2)*.6) if moving else 0
    def p(f,s,z=0): return (round(36+math.sin(angle)*f+math.cos(angle)*s),round(46-math.cos(angle)*f*.7+math.sin(angle)*s*.7-z))
    d.ellipse((17,44,57,62),fill=(18,26,19,85))
    # Pusher's hands extend to the handles; draw before the cart when behind it.
    human=unit_frame('hauler',facing,step,'push' if moving else 'push-stand')
    px,py=p(-18,0)
    if math.cos(angle)>0: im.alpha_composite(human,(px-28,py-38))
    for side in (-9,9):
        x,y=p(0,side)
        d.ellipse((x-3,y-3,x+3,y+6),fill='#463d2e',outline='#202a24',width=2)
        d.ellipse((x-2,y-2,x+2,y+5),outline='#a28b5c')
        for j in range(3):
            a=phase+j*math.pi/3
            d.line((round(x-math.cos(a)*2),round(y+1-math.sin(a)*4),round(x+math.cos(a)*2),round(y+1+math.sin(a)*4)),fill='#c5ac72')
        d.line((*p(-6,side/2,3),*p(-17,side/2,3)),fill='#ae8e54',width=2)
    d.polygon([p(f,s,4+bounce) for f,s in ((-7,-7),(8,-7),(8,7),(-7,7))],fill='#8e7042',outline='#302f23')
    for f in (-5,-1,3,7): d.line((*p(f,-7,4+bounce),*p(f,7,4+bounce)),fill='#b99a60')
    for side in (-7,7):
        d.polygon([p(-7,side,4+bounce),p(8,side,4+bounce),p(8,side,10+bounce),p(-7,side,10+bounce)],fill='#8e6c3b',outline='#3d3425')
        d.line((*p(-7,side,10+bounce),*p(8,side,10+bounce)),fill='#d1b375')
    if math.cos(angle)<=0: im.alpha_composite(human,(px-28,py-38))
    return im
