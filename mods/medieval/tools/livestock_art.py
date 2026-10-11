"""Editable articulated livestock models, projected onto the game's 2:1 pixel grid.

All four ages have their own anatomy. Geometry, coat patches and wool stay attached
to the body throughout the gait; no frame is a translated or scaled adult sprite.
"""
import math
from PIL import Image, ImageDraw

MODES = ('idle', 'walk', 'graze', 'handled', 'led')
KINDS = ('cow', 'calf', 'sheep', 'lamb')


def livestock(kind, facing, frame, mode='idle'):
    cow = kind in ('cow', 'calf'); young = kind in ('calf', 'lamb')
    co, si = math.cos(facing*math.tau/8), math.sin(facing*math.tau/8)
    phase = frame*math.tau/8; moving = mode in ('walk','led')
    # Fixed hoof anchor (32,44), shared with the world actor and rope attachments.
    scale = 3; im = Image.new('RGBA', (64*scale, 56*scale)); draw = ImageDraw.Draw(im)
    surfaces = []
    def world(p):
        u,v,z = p
        return u*co-v*si, u*si+v*co, z
    def screen(p):
        x,y,z = world(p)
        return (round((32+x)*scale), round((44+y*.5-z)*scale))
    def depth(p):
        _,y,z = world(p); return 2*y+z
    def line(points, color, width=1):
        surfaces.append((sum(depth(p) for p in points)/len(points)+.2, 'line', [screen(p) for p in points], color, round(width*scale)))
    def ball(center, radii, color, patch=False, wool=False):
        # Small shaded ellipsoid mesh: directional light, rounded volume, no black box outline.
        rings, sectors = 6, 12
        verts = []
        for j in range(rings+1):
            lat = -math.pi/2+j*math.pi/rings
            verts.append([(center[0]+radii[0]*math.cos(lat)*math.cos(k*math.tau/sectors),
                           center[1]+radii[1]*math.cos(lat)*math.sin(k*math.tau/sectors),
                           center[2]+radii[2]*math.sin(lat)) for k in range(sectors)])
        for j in range(rings):
            for k in range(sectors):
                points = [verts[j][k],verts[j][(k+1)%sectors],verts[j+1][(k+1)%sectors],verts[j+1][k]]
                mid = tuple(sum(p[n] for p in points)/4 for n in range(3))
                normal = tuple((mid[n]-center[n])/radii[n]**2 for n in range(3))
                nx,ny,nz = world(normal); length = max(.001, math.sqrt(nx*nx+ny*ny+nz*nz))
                # Facing camera (0,2,1); discard back faces before sorting.
                if 2*ny+nz < 0: continue
                light = max(0,(-nx*.42-ny*.28+nz*.86)/length)
                base = color
                if patch:
                    u,v,z=mid
                    if ((u+4.8)**2/20+(v-1.5)**2/14 < 1 or
                        (u-5.2)**2/11+(v+2.1)**2/17+(z-center[2]-1)**2/65 < 1):
                        base = (123,72,40) if young else (112,64,35)
                grain = ((j*7+k*11)%5-2)*(1.7 if wool else .7)
                factor = .69+light*.37
                c = tuple(max(0,min(255,round(b*factor+grain))) for b in base)+(255,)
                surfaces.append((depth(mid), 'poly', [screen(p) for p in points], c, 0))
    def limb(a,b,r,color):
        # Articulated joints and tapered shins, continuous across the contact/recovery cycle.
        line([a,b],color,r*1.7)
        ball(a,(r,r,r),color); ball(b,(r*.7,r*.7,r*.7),color)

    body_len, width, body_z, body_h = ((10.2,5,15,6) if cow and not young else
        (7.2,3.5,11.8,4.4) if cow else (8,4.7,10.5,5.3) if not young else (5.5,3.2,8,3.7))
    coat = (224,209,171) if cow else (231,225,199)
    skin = (156,109,67) if cow else (100,91,72) if not young else (182,157,117)
    bob = math.cos(phase*2)*(.45 if not young else .7) if moving else math.sin(phase)*.22
    head_drop = (0,.28,.8,1,1,.85,.35,0)[frame] if mode == 'graze' else 0
    # One four-beat walking cycle; youngsters have a lighter, higher step.
    for u,v,offset in [(-body_len*.64,-width*.62,0), (body_len*.64,-width*.62,math.pi),
                        (-body_len*.64,width*.62,math.pi*.5),(body_len*.64,width*.62,math.pi*1.5)]:
        t=phase+offset; stride=math.sin(t)*(2.9 if cow else 2.1) if moving else 0
        lift=max(0,math.cos(t))*(2 if young else 1.3) if moving else 0
        hip=(u,v,body_z-1+bob); knee=(u+stride*.35-.5,v,(body_z-body_h)*.5+2+lift*.35)
        foot=(u+stride,v,1+lift)
        limb(hip,knee,1.1 if cow and not young else .8,skin)
        limb(knee,foot,.72 if cow else .65,(203,187,153) if cow else skin)
        ball((foot[0]+.35,v,foot[2]-.2),(1.25 if cow else .85,.9,.7),(62,54,42))
    ball((-.6,0,body_z+bob),(body_len,width,body_h),coat,patch=cow)
    if cow:
        ball((body_len*.52,0,body_z+bob+.4),(3.7 if not young else 2.6,width*.94,body_h*.95),coat,patch=True)
        if not young:
            ball((-4,0,8.5+bob),(3,2.8,1.7),(194,145,117))
            for v in (-1.4,1.4): line([(-4,v,8),(-4,v,6.5)],(158,108,83),.65)
        flick=math.sin(phase)*2.2
        tail=[(-body_len,0,body_z+1+bob),(-body_len-2,1+flick,body_z-4),(-body_len-1,2+flick,body_z-8)]
        line(tail,(145,104,63),.9);ball(tail[-1],(.85,.8,1.5),(89,63,40))
    else:
        # Dense attached locks outline the fleece; the lamb's coat is short and soft.
        for j in range(3):
            for i in range(6 if not young else 5):
                u=-body_len*.78+i*body_len*1.56/(5 if not young else 4)
                v=(j-1)*width*.65
                z=body_z+bob+body_h*math.sqrt(max(.08,1-(u/body_len)**2-(v/(width*1.3))**2))
                r=1.9 if not young else 1.35
                ball((u,v,z-.6),(r,r*.88,r*.8),coat,wool=True)
        ball((-body_len-.8,0,body_z-1+bob),(1.3,.95,1.8 if young else 1.2),coat)

    hu=body_len+(2.8 if cow else 2); hz=(18 if cow and not young else 15 if cow else 13 if not young else 10.5)+bob
    hu+=head_drop*2; hz-=head_drop*(hz-4)
    if mode=='handled': hz+=math.sin(phase)*.5
    ball((body_len*.82,0,(body_z+hz)*.5-1),(3.2 if cow else 2.3,width*.58,4 if cow else 2.8),skin)
    ball((hu,0,hz),(3.4 if cow else 2.5,2.8 if cow else 2,3.6 if cow else 2.7),coat if cow else skin)
    # Long muzzle / soft nose, pale forehead blaze and two visible nostrils.
    muzzle=(hu+(3 if cow else 2),0,hz-1.6)
    ball(muzzle,(2.6 if cow else 1.8,2.55 if cow else 1.7,1.8),(194,148,126) if cow else (122,106,82) if young else (72,67,54))
    if cow: ball((hu+1,0,hz+1),(2,1.1,2.8),(238,226,195))
    for side in (-1,1):
        twitch=math.sin(phase+side)*(.9-head_drop*.5)
        # Calves and lambs have oversized ears, cows alone have small curved horns.
        ball((hu-1,side*(3.5 if cow else 2.9),hz+1+twitch),
             (2.2 if young else 1.8,1.35,.7),skin)
        line([(hu-.7,side*2.8,hz+1.2+twitch),(hu-1,side*4.3,hz+1.2+twitch)],(206,158,131),.6)
        ball((hu+1,side*(2.55 if cow else 1.8),hz+1),( .45,.35,.45),(36,34,25))
        if frame!=6 or moving: ball((hu+.92,side*(2.68 if cow else 1.94),hz+1.2),(.16,.16,.16),(253,242,209))
        if cow:
            ball((muzzle[0]+1.6,side*1.5,muzzle[2]+.2),(.4,.45,.3),(102,72,57))
            if not young:
                line([(hu-1,side*1.6,hz+2.8),(hu-2,side*2.9,hz+5),(hu-.7,side*3,hz+5.5)],(224,205,163),1)
    if mode in ('handled','led'):
        # Leather halter follows the same muzzle anchor as the in-world lead rope.
        line([(hu+2,-2.2,hz-1),(hu+3.5,0,hz-2.8),(hu+2,2.2,hz-1)],(116,78,40),.7)
    draw.ellipse((round((32-body_len-2)*scale),41*scale,round((32+body_len+3)*scale),47*scale),fill=(25,31,20,48))
    for _,shape,points,color,width in sorted(surfaces,key=lambda s:s[0]):
        if shape=='poly':draw.polygon(points,fill=color)
        else:draw.line(points,fill=color,width=width)
    return im.resize((64,56),Image.Resampling.BOX)
