"""Small market stall, drawn directly on the game's 48x24 isometric grid."""
import math
from PIL import Image, ImageDraw


def stall_frame(frame):
    scale = 3
    im = Image.new('RGBA', (184 * scale, 184 * scale))
    d = ImageDraw.Draw(im)
    def p(u, v, z=0):
        return round((92 + 24 * ((u - 1) - (v - .5))) * scale), round((140 + 12 * ((u - 1) + (v - .5)) - z) * scale)
    def poly(points, fill, outline='#493d2b'):
        d.polygon([p(*a) for a in points], fill=fill, outline=outline, width=scale)
    def line(points, fill, width=1):
        d.line([p(*a) for a in points], fill=fill, width=round(width * scale))
    def post(u, v, height):
        line([(u, v, 0), (u, v, height)], '#463826', 3)
        line([(u-.035, v-.035, 1), (u-.035, v-.035, height)], '#b1905a', 1)
    phase = frame / 24 * math.tau
    poly([(0,0,0),(2,0,0),(2,1,0),(0,1,0)], (84,72,43,90), None)
    # Plank platform and rear posts; everything remains registered to the ground.
    poly([(.15,.05,2),(1.85,.05,2),(1.85,.9,2),(.15,.9,2)], '#837048')
    for u in (.4,.75,1.1,1.45): line([(u,.06,2),(u,.9,2)], '#ba9b64', .6)
    for u in (.18,1.82): post(u,.08,41)
    # Shopkeeper behind the counter: apron, cap, face and moving forearms.
    sway = math.sin(phase) * .025
    u,v = .95+sway,.32
    poly([(u-.11,v,8),(u+.13,v,8),(u+.13,v,22),(u-.11,v,22)], '#657d78')
    poly([(u-.09,v+.01,8),(u+.1,v+.01,8),(u+.07,v+.01,18),(u-.08,v+.01,18)], '#d3bd8d')
    x,y = p(u,v,26)
    d.ellipse((x-3*scale,y-4*scale,x+3*scale,y+3*scale),fill='#c8a47a',outline='#675039',width=scale)
    d.polygon([(x-4*scale,y-3*scale),(x-2*scale,y-6*scale),(x+2*scale,y-6*scale),(x+4*scale,y-3*scale)],fill='#785a37')
    reach = .5-.5*math.cos(phase)
    for sign in (-1,1):
        hand=(u+sign*(.18+.08*reach),.61,14+2*reach)
        line([(u+sign*.12,v,21),hand], '#46544c', 2.5)
        line([hand,(hand[0],hand[1],hand[2]+1)], '#d6b283', 1.8)
    # Front counter, empty baskets. Spendable goods are drawn from the live stock.
    poly([(.23,.57,14),(1.74,.57,14),(1.74,.94,14),(.23,.94,14)], '#b39360')
    poly([(.23,.94,4),(1.74,.94,4),(1.74,.94,14),(.23,.94,14)], '#806039')
    for u in (.4,.65,.9,1.15,1.4,1.65): line([(u,.945,5),(u,.945,13)], '#bd985f', .7)
    line([(.23,.94,14),(1.74,.94,14)], '#e0c18a', 1)
    # Cream and blue canvas: the blue hue is remapped to the empire's livery.
    for i in range(8):
        a,b=.03+i*.243,.03+(i+1)*.243
        poly([(a,-.03,42),(b,-.03,42),(b,1.02,35),(a,1.02,35)], '#d0b886' if i%2==0 else '#3c6c9a', None)
        poly([(a,1.02,35),(b,1.02,35),(b,1.02,31.5),(a,1.02,32+math.sin(phase+i*.3)*.3)], '#b59d6f' if i%2==0 else '#2e507d', None)
    line([(.03,-.03,42),(1.97,-.03,42),(1.97,1.02,35)], '#e1cb9b', .8)
    line([(.03,-.03,42),(.03,1.02,35),(1.97,1.02,35)], '#604530', .8)
    for u in (.18,1.82): post(u,.99,35)
    return im.resize((184,184),Image.Resampling.LANCZOS)
