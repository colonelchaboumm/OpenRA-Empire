"""Categorized construction chrome and native-size icons, after all content generators."""
from pathlib import Path
import re
from PIL import Image
from wilderness_art import save

MOD=Path(__file__).resolve().parents[1]

def main(output=None, wire=True):
    global MOD
    if output is not None: MOD=Path(output)
    path=MOD/'chrome/ingame-player.yaml'
    text=path.read_text(encoding='utf-8')
    if 'MedievalConstructionMenuLogic' not in text:
        text=text.replace('MedievalCampLogic','MedievalCampLogic, MedievalConstructionMenuLogic',1)
    text=re.sub(r'^\t{4}(?:Button|Label)@MD_MENU_[^\n]*\n(?:\t{5,}[^\n]*\n|\n)*','',text,flags=re.M)
    lines=[]
    def item(kind,id,x,y,w,h,extra=(),depth=4):
        t='\t'*depth
        lines.extend([f'{t}{kind}@MD_MENU_{id}:',f'{t}\tX: {x}',f'{t}\tY: {y}',f'{t}\tWidth: {w}',f'{t}\tHeight: {h}'])
        if kind in ('Button','Label'):lines.append(f'{t}\tFont: TinyBold')
        if kind=='Button':lines.append(f'{t}\tBackground: md-button')
        lines.extend(f'{t}\t{v}' for v in extra)
    for i in range(6):item('Button',f'TAB_{i}',5+i%3*77,72+i//3*27,74,24)
    item('Label','TITLE',7,128,224,20)
    for i in range(8):
        item('Button',f'ITEM_{i}',5+i%2*116,154+i//2*46,112,42,
             ['TooltipContainer: TOOLTIP_CONTAINER','TooltipTemplate: BUTTON_WITH_DESC_HIGHLIGHT_TOOLTIP','Children:'])
        item('Sprite',f'ITEM_{i}_ICON',3,4,32,34,depth=6)
        item('Label',f'ITEM_{i}_NAME',37,4,73,17,depth=6)
        item('Label',f'ITEM_{i}_COST',37,22,73,16,['TextColor: B6AE91'],depth=6)
    item('Button','PREV',5,340,86,22)
    item('Label','PAGE',94,340,46,22)
    item('Button','NEXT',145,340,88,22)
    item('Label','HINT',7,366,224,28,['WordWrap: True','VAlign: Top'])
    item('Button','ROTATE',5,397,112,24)
    item('Button','REMOVE',121,397,112,24)
    item('Label','STOCK',7,426,224,28,['WordWrap: True','VAlign: Top'])
    anchor='\t\t\t\tButton@MD_CART_CREATE:'
    assert text.count(anchor)==1
    text=text.replace(anchor,'\n'.join(lines)+'\n'+anchor)
    path.write_text(text,encoding='utf-8',newline='\n')

    # The C# catalog is the single source of truth for category order and sprite indices.
    catalog=(MOD.parents[1]/'OpenRA.Mods.Medieval/ConstructionMenu.cs').read_text(encoding='utf-8')
    slots=int(re.search(r'IconSlotsPerCategory = (\d+)',catalog)[1])
    groups=re.findall(r'new\("(?!md\.)[^\n]+, \[\n(.*?)\]\)',catalog,re.S)
    assert len(groups)==6, 'Expected six construction menu categories'
    icons=[]
    for group in groups:
        types=re.findall(r'new\("md\.([^\"]+)"',group)
        assert 1<=len(types)<=slots
        for name in types:
            sheet=Image.open(MOD/'art'/f'{name}.png').convert('RGBA')
            meta=Image.open(MOD/'art'/f'{name}.png').info
            if 'Frame[0]' in meta:
                x,y,w,h=map(int,meta['Frame[0]'].split(';')[0].split(','));sprite=sheet.crop((x,y,x+w,y+h))
            else:
                w,h=map(int,meta['FrameSize'].split(','));sprite=sheet.crop((0,0,w,h))
            sprite=sprite.crop(sprite.getbbox());sprite.thumbnail((32,34),Image.Resampling.LANCZOS)
            icon=Image.new('RGBA',(32,34));icon.alpha_composite(sprite,((32-sprite.width)//2,(34-sprite.height)//2));icons.append(icon)
        icons.extend(Image.new('RGBA',(32,34)) for _ in range(slots-len(types)))
    import wilderness_art
    previous=wilderness_art.MOD;wilderness_art.MOD=MOD
    save('construction-icons',icons);wilderness_art.MOD=previous
    (MOD/'sequences/construction-menu.yaml').write_text(f'md.construction-icons:\n\tDefaults:\n\t\tFilename: medieval|art/construction-icons.png\n\tidle:\n\t\tLength: {slots*len(groups)}\n',encoding='utf-8',newline='\n')
    if not wire:return
    for manifest in MOD.parent.glob('medieval*/mod.yaml'):
        t=manifest.read_text(encoding='utf-8')
        if 'medieval|sequences/construction-menu.yaml' not in t:
            t=t.replace('\tmedieval|sequences/camping.yaml','\tmedieval|sequences/camping.yaml\n\tmedieval|sequences/construction-menu.yaml')
        manifest.write_text(t,encoding='utf-8',newline='\n')
    test=MOD.parent/'medieval-construction-menu';test.mkdir(exist_ok=True)
    t=(MOD/'mod.yaml').read_text(encoding='utf-8').replace('\tmedieval|camping.yaml','\tmedieval|camping.yaml\n\tmedieval|construction-menu-tests.yaml',1).replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4')
    (test/'mod.yaml').write_text(t,encoding='utf-8',newline='\n')

if __name__=='__main__':main()
