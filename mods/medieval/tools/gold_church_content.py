"""Pack generated building masters and extend the native pixel sprites/UI for gold."""
from pathlib import Path
import math
import re
from PIL import Image, ImageDraw
from wilderness_art import save
from animation_art import unit_frame, UNIT_MODES, HERD_MODES

MOD=Path(__file__).resolve().parents[1]

def painted(name,frame,size,bottom):
    image=Image.open(MOD/'art-source'/f'{name}-isometric.png').convert('RGBA')
    box=image.getchannel('A').point(lambda a:255 if a>12 else 0).getbbox()
    image=image.crop(box);image.thumbnail(size,Image.Resampling.LANCZOS)
    result=Image.new('RGBA',frame);result.alpha_composite(image,((frame[0]-image.width)//2,bottom-image.height));return result

def site(frame,ground,cells):
    im=Image.new('RGBA',frame);d=ImageDraw.Draw(im)
    def p(x,y,z=0):return (round(frame[0]/2+(x-y)*24),round(ground+(x+y-cells)*12-z))
    corners=[(.15,.15),(cells-.15,.15),(cells-.15,cells-.15),(.15,cells-.15)]
    d.polygon([p(x,y) for x,y in corners],fill='#7e7254',outline='#504637')
    for x,y in corners:d.line([p(x,y),p(x,y,25)],fill='#bb935a',width=3)
    for i in range(4):d.line([p(*corners[i],20),p(*corners[(i+1)%4],20)],fill='#aa8654',width=2)
    return im

def bar(d,x,y,scale=1):
    pts=[(x,y),(x+5*scale,y-2*scale),(x+11*scale,y),(x+6*scale,y+3*scale)]
    d.polygon([(a,b+3*scale) for a,b in pts]+list(reversed(pts)),fill='#a87614',outline='#725018')
    d.polygon(pts,fill='#e3b631',outline='#946717');d.line([pts[0],pts[1],pts[2]],fill='#fff097')

def gold_stock(n):
    im=Image.new('RGBA',(48,64));d=ImageDraw.Draw(im)
    for i in range(n):
        col=i%4;row=i//4%4;layer=i//16
        bar(d,6+col*6-row*3,49+col*2+row*2-layer*5,.6)
    return im

def main():
    save('church',[painted('church',(216,264),(146,194),256),site((216,264),220,3)])
    save('goldmine',[painted('goldmine',(184,184),(108,92),164),site((184,184),140,2)])
    from gold_deposit_art import main as pack_gold_deposit
    pack_gold_deposit()
    icon=Image.new('RGBA',(16,16));bar(ImageDraw.Draw(icon),1,7);save('gold-icon',[icon])
    save('gold-piles',[gold_stock(n).resize((32,42),Image.Resampling.NEAREST) for n in [5,12,24,40]])
    save('gold-stock',[gold_stock(n) for n in range(101)])
    modes=UNIT_MODES+HERD_MODES+('water-run','water-empty-run','water-fill','water-pour','water-stand','water-empty-stand')
    seq=''
    for name,offset in [('church',-88),('goldmine',-48)]:
        seq+=f'md.{name}:\n\tDefaults:\n\t\tFilename: medieval|art/{name}.png\n\t\tOffset: 0, {offset}\n\tidle:\n\tconstruction:\n\t\tStart: 1\n\n'
    seq+='md.gold:\n\tidle:\n\t\tFilename: medieval|art/gold.png\n\t\tOffset: 0, -12\n\tempty:\n\t\tFilename: medieval|art/gold-rubble.png\n\t\tOffset: 0, -12\n\n'
    seq+='md.goldrubble:\n\tDefaults:\n\t\tFilename: medieval|art/gold-rubble.png\n\t\tOffset: 0, -12\n\tidle:\n\n'
    for name,model in [('goldminer','stonecutter'),('priest','priest')]:
        save(name,[unit_frame(model,f,i,m) for m in modes for f in range(8) for i in range(8)])
        seq+=f'md.{name}:\n\tDefaults:\n\t\tFilename: medieval|art/{name}.png\n\t\tOffset: 0, -12\n'
        for i,m in enumerate(modes):seq+=f'\t{m}:\n\t\tStart: {i*64}\n\t\tLength: 8\n\t\tFacings: 8\n\t\tTick: 120\n'
        if name=='priest':
            from priest_animation import priest_heal_frame, HEAL_FRAMES
            save('priest-heal',[priest_heal_frame(f,i) for f in range(8) for i in range(HEAL_FRAMES)])
            seq+='\theal:\n\t\tFilename: medieval|art/priest-heal.png\n\t\tLength: 16\n\t\tFacings: 8\n\t\tTick: 120\n'
        seq+='\n'
    seq+='md.resources:\n\tgold:\n\t\tFilename: medieval|art/gold-icon.png\n\t\tStart: 0\n\nmd.piles:\n\tgold:\n\t\tFilename: medieval|art/gold-piles.png\n\t\tStart: 0\n\t\tLength: 4\n\nmd.gold-stock:\n\tidle:\n\t\tFilename: medieval|art/gold-stock.png\n\t\tLength: 101\n'
    (MOD/'sequences/gold-church.yaml').write_text(seq,encoding='utf-8',newline='\n')
    for path in MOD.parent.glob('medieval*/mod.yaml'):
        text=path.read_text(encoding='utf-8-sig')
        if '\tmedieval|rules.yaml\n' not in text:continue
        for section,item in [('Rules','gold-church.yaml'),('Sequences','sequences/gold-church.yaml'),('FluentMessages','fluent/gold-church.ftl')]:
            line='\tmedieval|'+item+'\n'
            if line in text:continue
            pattern=rf'(^{section}:\n(?:\t[^\n]*\n|\n)+)'
            text,count=re.subn(pattern,lambda m:m[0].rstrip('\n')+'\n'+line+'\n',text,count=1,flags=re.M)
            assert count,(path,section)
        path.write_text(text,encoding='utf-8',newline='\n')
    path=MOD/'chrome/ingame-player.yaml';text=path.read_text(encoding='utf-8')
    if 'Label@MD_RESOURCE_4:' not in text:
        text=text.replace('\t\t\t\tLabel@MD_POP:', '\t\t\t\tLabel@MD_RESOURCE_4:\n\t\t\t\t\tX: 143\n\t\t\t\t\tY: 46\n\t\t\t\t\tWidth: 94\n\t\t\t\t\tHeight: 20\n\t\t\t\t\tFont: TinyBold\n\t\t\t\t\tTextColor: F0CB5E\n\t\t\t\tLabel@MD_POP:',1)
    for prefix,ids,width,spacing in [('MD_CART_CARGO_', [1,0,3,4],94,99),('MD_TRADE_EXPORT_',[1,0,3,2,4],74,79)]:
        for i,r in enumerate(ids):
            pattern=rf'(\t{{4}}Button@{prefix}{r}:\n)(\t{{5}}[^\n]*\n)+'
            old=re.search(pattern,text)
            if old:
                block=re.sub(r'\tX: \d+',f'\tX: {9+i*spacing}',old[0]);block=re.sub(r'\tWidth: \d+',f'\tWidth: {width}',block)
                text=text[:old.start()]+block+text[old.end():]
            else:
                y=28 if 'CART' in prefix else 122
                block=f'\t\t\t\tButton@{prefix}{r}:\n\t\t\t\t\tX: {9+i*spacing}\n\t\t\t\t\tY: {y}\n\t\t\t\t\tWidth: {width}\n\t\t\t\t\tHeight: 26\n\t\t\t\t\tFont: TinyBold\n\t\t\t\t\tBackground: md-button\n\t\t\t\t\tTooltipContainer: TOOLTIP_CONTAINER\n'
                anchor='\t\t\t\tLabel@'+('MD_CART_STOCK:' if 'CART' in prefix else 'MD_TRADE_IMPORTS:')
                text=text.replace(anchor,block+anchor,1)
    if 'Background@MD_CHURCH_PANEL:' not in text:
        text+='\t\tBackground@MD_CHURCH_PANEL:\n\t\t\tX: 12\n\t\t\tY: 154\n\t\t\tWidth: 410\n\t\t\tHeight: 218\n\t\t\tBackground: md-panel\n\t\t\tChildren:\n'
        for kind,name,y,h in [('Label','TITLE',8,22),('Label','STATUS',36,22),('Label','HINT',66,102),('Button','RECRUIT',180,28)]:
            text+=f'\t\t\t\t{kind}@MD_CHURCH_{name}:\n\t\t\t\t\tX: 9\n\t\t\t\t\tY: {y}\n\t\t\t\t\tWidth: 392\n\t\t\t\t\tHeight: {h}\n\t\t\t\t\tFont: TinyBold\n'
            if kind=='Button':text+='\t\t\t\t\tBackground: md-button\n'
            if name=='HINT':text+='\t\t\t\t\tWordWrap: True\n\t\t\t\t\tVAlign: Top\n'
    path.write_text(text,encoding='utf-8',newline='\n')
    # Refresh only the category menu from its source catalog; retain all other panels.
    from construction_menu_content import main as menu
    menu(wire=False)

if __name__=='__main__':main()
