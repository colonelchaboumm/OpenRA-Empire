"""Articulated native priest: reading, raising the cross, blessing, recovery.

Sixteen poses in eight directions; feet remain on the same world anchor as stand/run.
"""
import math
from PIL import Image, ImageDraw

HEAL_FRAMES = 16


def priest_heal_frame(facing, step):
    im = Image.new('RGBA', (56, 64))
    d = ImageDraw.Draw(im)
    angle = -facing * math.tau / 8
    dx, dy = math.sin(angle), -math.cos(angle)
    side = 1 if dx >= 0 else -1
    rear = dy < -.25
    phase = step * math.tau / HEAL_FRAMES
    lift = (0, 0, 1, 2, 4, 6, 8, 9, 9, 8, 7, 6, 4, 2, 1, 0)[step]
    bend = (1, 1, 2, 2, 1, 1, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1)[step]
    sweep = (0, 0, 0, 0, 0, 0, 0, -1, -2, -1, 1, 2, 1, 0, 0, 0)[step]
    x, y = 28 + round(dx * bend * .7), 27 + bend
    ink, robe, shade = '#494536', '#efe5c9', '#b6aa89'
    def p(x, y): return round(x), round(y)
    def line(a, b, color, width=1): d.line([p(*a), p(*b)], fill=color, width=width)
    d.ellipse((20, 42, 38, 47), fill=(17, 24, 18, 75))
    for s in (-1, 1):
        line((28+s*2, 40), (28+s*2, 45), '#64563e', 3)
        line((27+s*2, 45), (30+s*2, 45), '#35382c', 2)
    # Folded robe and the empire-colored stole retain the normal silhouette.
    d.polygon([p(x-4,y),p(x+4,y),p(34,43),p(28,45),p(22,43)], fill=shade, outline=ink)
    d.polygon([p(x-3,y+1),p(x+2,y+1),(30,43),(25,43)], fill=robe)
    line((x-1,y+4),(25,42),'#fff2d5')
    line((x+2,y+5),(31,42),'#938c73')
    for s in (-1,1):
        line((x+s*2,y+1),(28+s*3,41),'#994399',2)
        line((27+s*3,41),(29+s*3,41),'#d2ba72')
    line((x-4,y+9),(x+4,y+9),'#75634a')
    # Book arm, raised arm and elbows are all posed individually.
    book = p(x-side*5+dx, y+7+dy)
    hand = p(x+side*(6+sweep)+dx*2, y+7+dy*2-lift)
    for s, target in ((-side,book),(side,hand)):
        shoulder=p(x+s*4,y+2)
        elbow=p((shoulder[0]+target[0])/2+s, (shoulder[1]+target[1])/2+2)
        d.line([shoulder,elbow,target],fill=ink,width=4)
        d.line([shoulder,elbow,target],fill=robe,width=3)
        d.point(target,fill='#d8b58a')
        line((target[0]-1,target[1]+1),(target[0]+1,target[1]+1),'#b3a079')
    # Head bows toward the book then rises for the blessing.
    hx, hy = p(x+dx, y-7+(.5 if step<5 else 0))
    d.ellipse((hx-3,hy-2,hx+3,hy+5),fill='#9c805e' if rear else '#d3b087',outline=ink)
    d.arc((hx-3,hy-2,hx+3,hy+5),175,365,fill='#8b806b',width=2)
    d.ellipse((hx-1,hy-1,hx+1,hy+1),fill='#dbc49b')
    if not rear:
        d.point((hx+side*2,hy+2),fill='#514a37')
        line((hx-1,hy+4),(hx+1,hy+4),'#d9d0b0')
    # Two illuminated parchment pages; the outer page moves during the reading.
    bx, by = book
    page = round(math.sin(phase)*1.2)
    d.polygon([(bx-4,by-3),(bx,by-2),(bx+4,by-3),(bx+4,by+3),(bx,by+4),(bx-4,by+3)],fill='#80562f',outline=ink)
    d.polygon([(bx-3,by-2),(bx,by-1),(bx+3,by-2+page),(bx+3,by+2),(bx,by+3),(bx-3,by+2)],fill='#f4e6bd')
    line((bx,by-1),(bx,by+3),'#b5a174')
    for yy in (0,2):
        line((bx-3,by+yy),(bx-1,by+yy),'#95805a')
        line((bx+1,by+yy),(bx+2,by+yy),'#95805a')
    # Gold cross above the hand: dark edge, bronze side and a bright leading edge.
    cx, cy = hand[0], hand[1]-3
    line((cx,cy-5),(cx,cy+3),ink,3)
    line((cx-3,cy-2),(cx+3,cy-2),ink,3)
    line((cx,cy-5),(cx,cy+3),'#d4a53e',2)
    line((cx-3,cy-2),(cx+3,cy-2),'#e4c571',2)
    line((cx,cy-5),(cx,cy+1),'#fff1ba')
    d.point((cx-2,cy-2),fill='#fff8d6')
    if step in (7,8,9):
        radius = 3 if step==8 else 2
        line((cx-radius,cy-4),(cx+radius,cy-4),(255,236,172,150))
        line((cx,cy-4-radius),(cx,cy-4+radius),(255,246,209,160))
    d.point(hand,fill='#d8b58a')
    return im
