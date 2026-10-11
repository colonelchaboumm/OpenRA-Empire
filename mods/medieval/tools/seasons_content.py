"""Season/hunting content, with a packed painted lodge and native unit sprites."""
from pathlib import Path
from functools import lru_cache
from PIL import Image, ImageDraw
from wilderness_art import save, animal
from animation_art import unit_frame, UNIT_MODES

MOD = Path(__file__).resolve().parents[1]
WATER_MODES = ('water-run', 'water-empty-run', 'water-fill', 'water-pour', 'water-stand', 'water-empty-stand')
HUNT_MODES = ('hunt-stalk', 'hunt-aim', 'hunt-release', 'hunt-gather')

@lru_cache(maxsize=1)
def painted_lodge():
    # Pack the generated master without repainting it. Preserve its alpha.
    source = Image.open(MOD/'art-source/huntinglodge-painted.png').convert('RGBA')
    bounds = source.getchannel('A').point(lambda a:255 if a>12 else 0).getbbox()
    sprite = source.crop(bounds)
    sprite.thumbnail((104,114), Image.Resampling.LANCZOS)
    frame = Image.new('RGBA',(184,184))
    frame.alpha_composite(sprite,(92-sprite.width//2,164-sprite.height))
    return frame

def lodge(frame=0, site=False):
    if not site:
        return painted_lodge().copy()
    im = Image.new('RGBA', (184,184)); d = ImageDraw.Draw(im)
    def p(x,y,z=0): return (round(92+(x-y)*24), round(140+(x+y-2)*12-z))
    def poly(points, colour, outline='#40372a'): d.polygon([p(*a) for a in points], fill=colour, outline=outline)
    def line(points, colour, width=1): d.line([p(*a) for a in points], fill=colour, width=width)
    poly([(0,0),(2,0),(2,2),(0,2)], '#7b7551')
    # Foundations and a porch occupy the real 2x2 footprint.
    poly([(.12,.15,0),(1.72,.15,0),(1.72,1.55,0),(.12,1.55,0)], '#9b9278')
    for x,y in ((.15,.15),(1.72,.15),(.15,1.55),(1.72,1.55)): line([(x,y,0),(x,y,28)], '#bd985e',3)
    line([(.15,1.55,24),(1.72,1.55,24)],'#c9a572',3)
    return im

def main(wire=True):
    save('huntinglodge',[lodge(i) for i in range(8)]+[lodge(site=True)])
    modes=UNIT_MODES+WATER_MODES+HUNT_MODES
    save('hunter',[unit_frame('hunter',f,i,m) for m in modes for f in range(8) for i in range(8)])
    seq='md.huntinglodge:\n\tDefaults:\n\t\tFilename: medieval|art/huntinglodge.png\n\t\tOffset: 0, -48\n\tidle:\n\t\tLength: 8\n\t\tTick: 150\n\tconstruction:\n\t\tStart: 8\n\n'
    seq+='md.hunter:\n\tDefaults:\n\t\tFilename: medieval|art/hunter.png\n\t\tOffset: 0, -12\n'
    for i,mode in enumerate(modes): seq+=f'\t{mode}:\n\t\tStart: {i*64}\n\t\tLength: 8\n\t\tFacings: 8\n\t\tTick: 120\n'
    for kind in ('deer','rabbit'):
        save('wild-'+kind,[animal(kind,f,s,m) for m in (False,True) for f in range(8) for s in range(12)])
        save('wild-'+kind+'-fall',[animal(kind,f,0,False,fall=s/11) for f in range(8) for s in range(12)])
        seq+=f'\nmd.wild-{kind}-fall:\n\tidle:\n\t\tFilename: medieval|art/wild-{kind}-fall.png\n\t\tOffset: 0, {-24 if kind=="deer" else -12}\n\t\tLength: 96\n'
    (MOD/'sequences/seasons.yaml').write_text(seq,encoding='utf-8',newline='\n')
    (MOD/'fluent/seasons.ftl').write_text('medieval-huntinglodge = Cabane du chasseur\nmedieval-hunter = Chasseur\nmedieval-game-deer = Cerf — gibier limité\nmedieval-game-rabbit = Lapin — gibier limité\n',encoding='utf-8',newline='\n')
    if not wire: return
    for path in MOD.parent.glob('medieval*/mod.yaml'):
        text=path.read_text(encoding='utf-8')
        if '\tmedieval|firefighting.yaml\n' not in text: continue
        for anchor,extra in [('firefighting.yaml','seasons.yaml'),('sequences/firefighting.yaml','sequences/seasons.yaml'),('fluent/camping.ftl','fluent/seasons.ftl')]:
            if '\tmedieval|'+extra+'\n' not in text:
                assert '\tmedieval|'+anchor+'\n' in text,path
                text=text.replace('\tmedieval|'+anchor+'\n','\tmedieval|'+anchor+'\n\tmedieval|'+extra+'\n',1)
        path.write_text(text,encoding='utf-8',newline='\n')
    # Run after all older generators, including their optional UI rewrites.
    path=MOD/'chrome/ingame-player.yaml';text=path.read_text(encoding='utf-8')
    text=text.replace('Label@MD_CAMP_CLOCK:\n\t\t\tX: 440\n\t\t\tY: 14\n\t\t\tWidth: 300\n\t\t\tHeight: 24','Label@MD_CAMP_CLOCK:\n\t\t\tX: 440\n\t\t\tY: 5\n\t\t\tWidth: 300\n\t\t\tHeight: 40')
    if 'Background@MD_SEASON_CLOCK_BG:' not in text:
        text=text.replace('\t\tLabel@MD_CAMP_CLOCK:', '\t\tBackground@MD_SEASON_CLOCK_BG:\n\t\t\tX: 432\n\t\t\tY: 5\n\t\t\tWidth: 310\n\t\t\tHeight: 40\n\t\t\tBackground: md-panel\n\t\tLabel@MD_CAMP_CLOCK:')
    path.write_text(text,encoding='utf-8',newline='\n')
    from construction_menu_content import main as menu
    menu()
    test=MOD.parent/'medieval-seasons';test.mkdir(exist_ok=True)
    text=(MOD/'mod.yaml').read_text(encoding='utf-8').replace('\tmedieval|seasons.yaml','\tmedieval|seasons.yaml\n\tmedieval|seasons-tests.yaml',1)
    (test/'mod.yaml').write_text(text,encoding='utf-8',newline='\n')

if __name__=='__main__': main()
