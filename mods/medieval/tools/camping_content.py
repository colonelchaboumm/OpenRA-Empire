"""Persist day/night, sheep and camp commands through content regeneration."""
from pathlib import Path
import re
MOD=Path(__file__).resolve().parents[1]

def main():
    (MOD/'fluent/camping.ftl').write_text('''medieval-sheepfold = Bergerie
medieval-sheeppasture = Enclos de moutons
medieval-shepherd = Éleveur de moutons
medieval-weaver = Tisseur
medieval-tentweaver = Tisseur de tentes militaires
medieval-militarytent = Pavillon militaire
medieval-torch = Torche
''',encoding='utf-8')
    for path in MOD.parent.glob('medieval*/mod.yaml'):
        text=path.read_text(encoding='utf-8')
        for anchor,extras in (('wilderness.yaml',['camping.yaml','sheep-enclosures.yaml']),('sequences/wilderness.yaml',['sequences/camping.yaml']),('fluent/heraldry.ftl',['fluent/camping.ftl'])):
            for extra in extras:
                if '\tmedieval|'+extra not in text:text=text.replace('\tmedieval|'+anchor,'\tmedieval|'+anchor+'\n\tmedieval|'+extra,1)
        path.write_text(text,encoding='utf-8',newline='\n')
    path=MOD/'chrome/ingame-player.yaml';text=path.read_text(encoding='utf-8')
    if 'MedievalCampLogic' not in text:text=text.replace('MedievalHeraldryLogic','MedievalHeraldryLogic, MedievalCampLogic',1)
    if 'Button@MD_DECOR_TORCH:' not in text:
        text=text.replace('Background@MD_DECOR_PANEL:\n\t\t\tX: 12\n\t\t\tY: 154\n\t\t\tWidth: 410\n\t\t\tHeight: 250','Background@MD_DECOR_PANEL:\n\t\t\tX: 12\n\t\t\tY: 154\n\t\t\tWidth: 410\n\t\t\tHeight: 284')
        text=text.replace('\t\tBackground@MD_FORT_PANEL:', '\t\t\t\tButton@MD_DECOR_TORCH:\n\t\t\t\t\tX: 9\n\t\t\t\t\tY: 245\n\t\t\t\t\tWidth: 392\n\t\t\t\t\tHeight: 28\n\t\t\t\t\tFont: TinyBold\n\t\t\t\t\tBackground: md-button\n\t\tBackground@MD_FORT_PANEL:')
    text=re.sub(r'^\t\t(?:Button@MD_CAMP_OPEN|Label@MD_CAMP_CLOCK|Background@MD_CAMP_PANEL):\n(?:\t{3,}[^\n]*\n|\n)*','',text,flags=re.M)
    lines=[]
    def item(kind,id,x,y,w,h,extra=(),depth=2):
        t='\t'*depth
        lines.extend([f'{t}{kind}@MD_CAMP_{id}:',f'{t}\tX: {x}',f'{t}\tY: {y}',f'{t}\tWidth: {w}',f'{t}\tHeight: {h}'])
        if kind in ('Button','Label'):lines.append(f'{t}\tFont: TinyBold')
        if kind in ('Button','Background'):lines.append(f'{t}\tBackground: '+('md-button' if kind=='Button' else 'md-panel'))
        lines.extend(f'{t}\t{x}' for x in extra)
    item('Label','CLOCK',440,5,300,40,['VAlign: Top'])
    item('Button','OPEN',630,50,240,28)
    item('Background','PANEL',440,132,560,422,['Children:'])
    def child(kind,id,x,y,w,h,extra=()):item(kind,id,x,y,w,h,extra,4)
    child('Label','TITLE',14,10,530,22)
    child('Label','HINT',14,38,530,34,['WordWrap: True','VAlign: Top'])
    child('Label','STOCK',14,86,530,24)
    child('Label','RECIPE',14,116,530,34,['WordWrap: True','VAlign: Top'])
    child('Label','TARGET',14,158,430,30,['WordWrap: True','VAlign: Top'])
    child('Button','LESS',458,158,36,26);child('Button','MORE',503,158,36,26)
    child('Label','CLOTH_TARGET',14,194,430,30)
    child('Button','CLOTH_LESS',458,194,36,26);child('Button','CLOTH_MORE',503,194,36,26)
    for i,id in enumerate(('LOAD','DEPLOY','PACK','REST')):
        child('Button',id,14+i%2*268,238+i//2*36,258,30)
    child('Label','STATUS',14,322,530,48,['WordWrap: True','VAlign: Top'])
    child('Button','CLOSE',360,380,180,28)
    path.write_text(text.rstrip()+'\n'+'\n'.join(lines)+'\n',encoding='utf-8',newline='\n')
    test=MOD.parent/'medieval-camping';test.mkdir(exist_ok=True)
    text=(MOD/'mod.yaml').read_text(encoding='utf-8').replace('\tmedieval|camping.yaml','\tmedieval|camping.yaml\n\tmedieval|camping-tests.yaml',1).replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4')
    (test/'mod.yaml').write_text(text,encoding='utf-8',newline='\n')

if __name__=='__main__':main()
