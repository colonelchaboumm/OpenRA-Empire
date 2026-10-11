"""Commander recruitment, articulated infantry, camp orders and persistent chrome wiring."""
from pathlib import Path
import re
from wilderness_art import save
from animation_art import unit_frame, UNIT_MODES

MOD=Path(__file__).resolve().parents[1]

def main():
    save('commander',[unit_frame('commander',f,s,m) for m in UNIT_MODES for f in range(8) for s in range(8)])
    seq=['md.commander:','\tDefaults:','\t\tFilename: medieval|art/commander.png','\t\tOffset: 0, -12']
    for i,m in enumerate(UNIT_MODES):seq += [f'\t{m}:',f'\t\tStart: {i*64}','\t\tLength: 8','\t\tFacings: 8','\t\tTick: 120']
    (MOD/'sequences/commander.yaml').write_text('\n'.join(seq)+'\n',encoding='utf-8',newline='\n')
    (MOD/'fluent/commander.ftl').write_text('medieval-commander = Commandant\n',encoding='utf-8',newline='\n')
    for manifest in MOD.parent.glob('medieval*/mod.yaml'):
        text=manifest.read_text(encoding='utf-8')
        for prefix in ('','sequences/','fluent/'):
            suffix='ftl' if prefix=='fluent/' else 'yaml'
            extra=f'\tmedieval|{prefix}commander.{suffix}'
            if extra not in text:text=text.replace(f'\tmedieval|{prefix}camping.{suffix}',f'\tmedieval|{prefix}camping.{suffix}\n'+extra,1)
        manifest.write_text(text,encoding='utf-8',newline='\n')
    path=MOD/'chrome/ingame-player.yaml';text=path.read_text(encoding='utf-8')
    if 'Button@MD_BARRACKS_COMMANDER:' not in text:
        # Reuse the former full-width cancel row so the panel still fits 800px height.
        text=text.replace('Button@MD_BARRACKS_CANCEL:\n\t\t\t\t\tX: 9\n\t\t\t\t\tY: 400\n\t\t\t\t\tWidth: 392',
            'Button@MD_BARRACKS_CANCEL:\n\t\t\t\t\tX: 275\n\t\t\t\t\tY: 400\n\t\t\t\t\tWidth: 126')
        text=text.replace('\t\t\t\tButton@MD_BARRACKS_CANCEL:',
            '\t\t\t\tButton@MD_BARRACKS_COMMANDER:\n\t\t\t\t\tX: 9\n\t\t\t\t\tY: 400\n\t\t\t\t\tWidth: 258\n\t\t\t\t\tHeight: 26\n\t\t\t\t\tFont: TinyBold\n\t\t\t\t\tBackground: md-button\n\t\t\t\t\tTooltipContainer: TOOLTIP_CONTAINER\n\t\t\t\t\tTooltipTemplate: BUTTON_WITH_DESC_HIGHLIGHT_TOOLTIP\n\t\t\t\tButton@MD_BARRACKS_CANCEL:')
    text=re.sub(r'^\t\tBackground@MD_COMMAND_PANEL:\n(?:\t{3,}[^\n]*\n|\n)*','',text,flags=re.M)
    lines=['\t\tBackground@MD_COMMAND_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 234','\t\t\tBackground: md-panel','\t\t\tChildren:']
    def item(kind,id,x,y,w,h,extra=()):
        lines.extend([f'\t\t\t\t{kind}@MD_COMMAND_{id}:',f'\t\t\t\t\tX: {x}',f'\t\t\t\t\tY: {y}',f'\t\t\t\t\tWidth: {w}',f'\t\t\t\t\tHeight: {h}','\t\t\t\t\tFont: TinyBold'])
        if kind=='Button':lines.append('\t\t\t\t\tBackground: md-button')
        lines.extend('\t\t\t\t\t'+e for e in extra)
    item('Label','TITLE',9,10,392,20)
    item('Label','HINT',9,38,392,48,['WordWrap: True','VAlign: Top'])
    item('Button','DEPLOY',9,94,392,30)
    item('Button','REST',9,132,193,30);item('Button','WAKE',207,132,194,30)
    item('Label','STATUS',9,174,392,48,['WordWrap: True','VAlign: Top'])
    path.write_text(text.rstrip()+'\n'+'\n'.join(lines)+'\n',encoding='utf-8',newline='\n')
    test=MOD.parent/'medieval-commander';test.mkdir(exist_ok=True)
    manifest=(MOD/'mod.yaml').read_text(encoding='utf-8').replace('\tmedieval|commander.yaml','\tmedieval|commander.yaml\n\tmedieval|commander-tests.yaml',1).replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4')
    (test/'mod.yaml').write_text(manifest,encoding='utf-8',newline='\n')

if __name__=='__main__':main()
