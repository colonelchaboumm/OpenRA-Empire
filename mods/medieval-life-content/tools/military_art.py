"""Military building masters and small code-native equipment icons."""
from pathlib import Path
from PIL import Image, ImageDraw

def military_building(name):
    src=Image.open(Path(__file__).resolve().parents[1]/f'art-source/{name}-isometric.png').convert('RGBA')
    src=src.crop(src.getchannel('A').point(lambda a:255 if a>12 else 0).getbbox())
    src.thumbnail((140,136) if name=='forge' else (160,142),Image.Resampling.LANCZOS)
    im=Image.new('RGBA',(184,184));im.alpha_composite(src,((184-src.width)//2,(166 if name=='forge' else 172)-src.height))
    return im

def equipment_icons():
    result=[]
    for name in ('bow','sword','armor'):
        im=Image.new('RGBA',(16,16));d=ImageDraw.Draw(im)
        if name=='bow':
            d.line([(4,2),(9,4),(11,8),(9,12),(4,14)],fill='#d0a265',width=2)
            d.line((4,2,4,14),fill='#d6d4bd')
            d.line((2,8,14,8),fill='#a99d76');d.polygon([(14,8),(11,6),(11,10)],fill='#d4d9cf')
        elif name=='sword':
            d.polygon([(11,1),(13,2),(6,11),(4,9)],fill='#d1dad7',outline='#5b757a')
            d.line((3,8,8,12),fill='#c8a66a',width=2);d.line((3,13,5,10),fill='#9c7948',width=2)
        else:
            d.polygon([(3,3),(6,2),(7,4),(9,4),(10,2),(13,3),(14,6),(12,7),(12,14),(4,14),(4,7),(2,6)],fill='#899eaa',outline='#3f535b')
            d.line((8,5,8,12),fill='#d5dfd8');d.line((5,12,11,12),fill='#b8c5c5')
        result.append(im)
    return result
