"""Isometric pallets with an exact number of native resource objects (0..100)."""
import random
from PIL import Image, ImageDraw

MAX_STACK = 100


def floor_frames():
    result=[]
    colors=[(122,99,62),(139,120,78),(109,91,64),(116,112,92)]
    for resource,base in enumerate(colors):
        im=Image.new('RGBA',(48,24)); rng=random.Random(990+resource)
        for y in range(24):
            for x in range(48):
                u=(x+.5-24)/48+(y+.5)/24
                v=(y+.5)/24-(x+.5-24)/48
                if not 0<=u<1 or not 0<=v<1:continue
                shade=rng.randrange(-6,7)
                if int(v*24)%6==0:shade-=17
                if min(u,v,1-u,1-v)<.035:shade=-27
                im.putpixel((x,y),tuple(max(0,c+shade) for c in base)+(255,))
        result.append(im)
    return result


def stock_frame(resource,amount):
    im=Image.new('RGBA',(64,72)); d=ImageDraw.Draw(im)
    if amount==0:return im
    d.ellipse((12,44,53,64),fill=(25,28,20,65))
    for layer in range((amount+15)//16):
        slots=min(16,amount-layer*16)
        for slot in sorted(range(slots),key=lambda n:n%4+n//4):
            u,v=slot%4-1.5,slot//4-1.5
            x=round(32+(u-v)*5.5); y=round(52+(u+v)*2.75-layer*4)
            if resource==0:
                d.rounded_rectangle((x-4,y-3,x+4,y+1),radius=2,fill='#81613a',outline='#423725')
                d.line((x-2,y-2,x+3,y-2),fill='#bd9358')
                d.ellipse((x-4,y-3,x-1,y+1),fill='#c7a66a',outline='#74573a')
                d.point((x-3,y-1),fill='#977344')
            elif resource==1:
                d.ellipse((x-3,y-6,x+3,y+1),fill='#baaa78',outline='#62593a')
                d.line((x-1,y-4,x-2,y-1),fill='#e2d0a0')
                d.line((x-1,y-6,x+1,y-6),fill='#5d4a2c')
                d.line((x-1,y-7,x+1,y-7),fill='#d8be76')
            elif resource==2:
                d.polygon([(x-4,y-4),(x+1,y-5),(x+4,y-2),(x+4,y+1),(x-1,y+2),(x-4,y)],fill='#745335',outline='#413728')
                d.polygon([(x-3,y-4),(x+1,y-5),(x+3,y-2),(x-1,y-1)],fill='#a34f43')
                d.line((x-2,y-3,x,y-2),fill='#d69477')
                d.line((x-3,y-1,x+1,y+1),fill='#c7aa72')
            else:
                d.polygon([(x-4,y-3),(x+1,y-5),(x+4,y-3),(x+4,y),(x-1,y+2),(x-4,y)],fill='#8b9082',outline='#444f47')
                d.polygon([(x-3,y-3),(x+1,y-4),(x+3,y-3),(x-1,y-1)],fill='#c5c7b4')
                d.line((x-1,y,x-1,y+1),fill='#aeb49f')
    return im


def stock_frames():
    return [stock_frame(resource,amount) for resource in range(4) for amount in range(MAX_STACK+1)]
