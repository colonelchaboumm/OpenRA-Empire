"""Wire heraldry rules, generated assets and the market/caserne interface."""
from pathlib import Path
import re
MOD=Path(__file__).resolve().parents[1]

def main():
    for path in MOD.parent.glob('medieval*/mod.yaml'):
        text=path.read_text(encoding='utf-8')
        for anchor,extra in (('founding.yaml','heraldry.yaml'),('sequences/founding.yaml','sequences/heraldry.yaml'),('fluent/founding.ftl','fluent/heraldry.ftl')):
            entry='\tmedieval|'+extra
            if entry not in text:text=text.replace('\tmedieval|'+anchor,'\tmedieval|'+anchor+'\n'+entry,1)
        path.write_text(text,encoding='utf-8',newline='\n')
    path=MOD/'chrome/ingame-player.yaml';text=path.read_text(encoding='utf-8')
    if 'MedievalHeraldryLogic' not in text:text=text.replace('MedievalSiegeLogic','MedievalSiegeLogic, MedievalHeraldryLogic',1)
    if 'Button@MD_BARRACKS_STANDARD:' not in text:
        text=text.replace('Button@MD_BARRACKS_MESSENGER:\n\t\t\t\t\tX: 9\n\t\t\t\t\tY: 362\n\t\t\t\t\tWidth: 392','Button@MD_BARRACKS_MESSENGER:\n\t\t\t\t\tX: 9\n\t\t\t\t\tY: 362\n\t\t\t\t\tWidth: 193')
        text=text.replace('\t\t\t\tButton@MD_BARRACKS_CANCEL:', '\t\t\t\tButton@MD_BARRACKS_STANDARD:\n\t\t\t\t\tX: 207\n\t\t\t\t\tY: 362\n\t\t\t\t\tWidth: 194\n\t\t\t\t\tHeight: 32\n\t\t\t\t\tFont: TinyBold\n\t\t\t\t\tBackground: md-button\n\t\t\t\tButton@MD_BARRACKS_CANCEL:')
    # Replace only these two generated root widgets, preserving the rest of the interface.
    text=re.sub(r'^\t\t(?:Button@MD_HERALDRY_OPEN|Background@MD_HERALDRY_PANEL):\n(?:\t{3,}[^\n]*\n|\n)*', '', text, flags=re.M)
    if 'Button@MD_HERALDRY_OPEN:' not in text:
        lines=[]
        def item(kind,id,x,y,w,h,extra=(),depth=2):
            tab='\t'*depth
            lines.extend([f'{tab}{kind}@MD_HERALDRY_{id}:',f'{tab}\tX: {x}',f'{tab}\tY: {y}',f'{tab}\tWidth: {w}',f'{tab}\tHeight: {h}'])
            if kind in ('Button','Label','TextField'):lines.append(f'{tab}\tFont: TinyBold')
            if kind in ('Button','Background'):lines.append(f'{tab}\tBackground: md-button' if kind=='Button' else f'{tab}\tBackground: md-panel')
            lines.extend(f'{tab}\t{entry}' for entry in extra)
        item('Button','OPEN',440,94,280,28)
        item('Background','PANEL',440,132,560,462,['Children:'])
        def child(kind,id,x,y,w,h,extra=()):item(kind,id,x,y,w,h,extra,4)
        child('Label','TITLE',14,10,520,20)
        child('Label','HINT',14,38,530,36,['WordWrap: True','VAlign: Top'])
        child('Label','COUNTRY_LABEL',14,82,140,22)
        child('TextField','COUNTRY',155,76,385,30,['MaxLength: 32'])
        for i in range(6):
            x=14+(i%3)*178;y=126+(i//3)*72
            child('Button',f'EMBLEM_{i}',x,y,170,66,['Children:'])
            item('Sprite',f'ICON_{i}',2,8,64,48,(),6)
            item('Label',f'NAME_{i}',66,22,102,24,(),6)
        child('Label','LABEL',14,282,170,22)
        child('TextField','COLOR',185,276,156,30,['MaxLength: 7'])
        child('ColorBlock','SWATCH',355,277,70,28)
        for i in range(8):child('ColorBlock',f'PRESET_{i}',14+i*66,326,56,28)
        child('Label','STATUS',14,373,530,32,['WordWrap: True','VAlign: Top'])
        child('Button','APPLY',14,418,330,28)
        child('Button','CLOSE',355,418,185,28)
        text=text.rstrip()+'\n'+'\n'.join(lines)+'\n'
    path.write_text(text,encoding='utf-8',newline='\n')

if __name__=='__main__':main()
