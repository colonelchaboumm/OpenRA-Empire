"""Exact 4 by 3 footprint guide for the painted siege workshop master."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
im = Image.new('RGBA', (1024, 1024))
d = ImageDraw.Draw(im)
def p(x, y, z=0): return round(512+104*(x-y)), round(688+52*(x+y)-z)
def poly(points, color): d.polygon([p(*v) for v in points], fill=color, outline='#463a2a', width=3)
def beam(a,b): d.line([p(*a),p(*b)], fill='#654827', width=15)
poly([(-2,-1.5),(2,-1.5),(2,1.5),(-2,1.5)],'#a4946b')
poly([(-1.9,-1.4),(1.9,-1.4),(1.9,-1.4,190),(-1.9,-1.4,190)],'#a69c80')
poly([(1.9,-1.4),(1.9,0),(1.9,0,190),(1.9,-1.4,190)],'#8c8066')
for x in (-1.9,0,1.9): beam((x,0,0),(x,0,185))
beam((-1.9,0,185),(1.9,0,185))
poly([(-2,-1.5,200),(2,-1.5,200),(2,-.7,290),(-2,-.7,290)],'#72818b')
poly([(-2,-.7,290),(2,-.7,290),(2,.12,200),(-2,.12,200)],'#495c6a')
poly([(2,-1.5,200),(2,.12,200),(2,-.7,290)],'#b8aa8b')
for x in (-.2,1.5): beam((x,.8,0),(x,.8,172))
beam((-.3,.8,172),(1.6,.8,172))
im.save(ROOT/'art-source/siege-projection-guide.png')
