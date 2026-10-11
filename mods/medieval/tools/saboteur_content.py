"""Saboteur content, editable pixel poses and isolated preview/install workflow."""
from pathlib import Path
import argparse
import math
import re
import shutil
from PIL import Image, ImageDraw
import wilderness_art
from animation_art import unit_frame, UNIT_MODES, HERD_MODES

MOD=Path(__file__).resolve().parents[1]
RULES='''^MDBUILDING:
\tMedievalBurnable:

md.militia:
\tDetectCloaked@SABOTEUR:
\t\tDetectionTypes: MedievalSaboteur
\t\tRange: 4c0
\t\tRequiresCondition: !sleeping

md.saboteur:
\tInherits: ^MDFIGHTER
\t-MedievalSiegeCrew:
\t-MedievalSiegeCapture:
\t-CaptureManager:
\t-MedievalStockDisplay:
\tTooltip:
\t\tName: medieval-saboteur
\tHealth:
\t\tHP: 4500
\tMobile:
\t\tSpeed: 44
\tMedievalRecruitable:
\t\tFood: 35
\t\tMeat: 10
\t\tElite: True
\tMedievalSaboteur:
\tMedievalHerding:
\t\tCaptureTicks: 200
\tCloak:
\t\tInitialDelay: 25
\t\tCloakDelay: 125
\t\tDetectionTypes: MedievalSaboteur
\t\tUncloakOn: Attack, Damage
\t\tCloakedAlpha: 0.72
\t\tRequiresCondition: !sleeping
\tAutoTarget:
\t\tInitialStance: HoldFire
\t\tRequiresCondition: !sleeping && !herding && !sabotaging
\tAttackFrontal:
\t\tPauseOnCondition: sleeping || herding || sabotaging
\tArmament:
\t\tWeapon: MedievalSaboteurDagger
'''
WEAPONS='''MedievalSaboteurDagger:
\tInherits: MedievalClub
\tReloadDelay: 40
\tWarhead@Damage: TargetDamage
\t\tDamage: 500
'''


def fire(step):
    im=Image.new('RGBA',(96,128));d=ImageDraw.Draw(im);phase=step*math.tau/16
    # Bounded animated smoke and separate tongues of flame; no world simulation RNG.
    for i in range(12):
        age=(i/12+step/16)%1; y=100-age*82; x=48+math.sin(i*2+age*3)*12+age*14
        r=5+age*10;alpha=round(80*(1-age))
        d.ellipse((x-r,y-r,x+r,y+r),fill=(60,60,49,alpha))
        d.ellipse((x-r*.6,y-r*.7,x+r*.6,y+r*.5),fill=(98,92,75,alpha//2))
    for i in range(9):
        x=18+i*7;y=109+(i%3)*2; h=15+(i*13)%24+math.sin(phase+i*1.7)*7; sway=math.sin(phase*2+i)*4
        d.polygon([(x-5,y),(x-7,y-h*.3),(x-3+sway,y-h*.7),(x+sway,y-h),(x+2+sway,y-h*.55),(x+6,y-h*.2),(x+5,y)],fill='#b44c1f')
        d.polygon([(x-3,y),(x-4,y-h*.3),(x+sway,y-h*.8),(x+2,y-h*.35),(x+4,y)],fill='#f1982d')
        d.polygon([(x-1,y),(x-2,y-h*.2),(x+sway*.4,y-h*.5),(x+2,y)],fill='#ffe39a')
        spark=(i+step)%7
        d.point((round(x+sway),round(y-h-3-spark)),fill='#eac073')
    return im


def chrome(text):
    if 'MedievalSaboteurLogic' not in text:text=text.replace('MedievalHerdLogic','MedievalHerdLogic, MedievalSaboteurLogic',1)
    text=re.sub(r'^\t\tBackground@MD_SABOTEUR_PANEL:\n(?:\t{3,}[^\n]*\n|\n)*','',text,flags=re.M)
    start=text.index('\t\tBackground@MD_BARRACKS_PANEL:');end=text.index('\n\t\tBackground@',start+1)
    block=text[start:end].rstrip()
    block=re.sub(r'(?m)^\t{3}Y: \d+$','\t\t\tY: 142',block)
    block=re.sub(r'(?m)^\t{3}Height: \d+$','\t\t\tHeight: 552',block)
    def properties(id,values):
        nonlocal block
        pattern=rf'(^\t{{4}}(?:Button|Label)@{id}:\n)((?:\t{{5,}}[^\n]*\n?)*)'
        def edit(m):
            content=m[2]
            for key,value in values.items():content=re.sub(rf'(?m)^\t{{5}}{key}: [^\n]+',f'\t\t\t\t\t{key}: {value}',content)
            return m[1]+content
        block=re.sub(pattern,edit,block,flags=re.M)
    properties('MD_BARRACKS_COMMANDER',{'Width':392})
    properties('MD_BARRACKS_CANCEL',{'Y':432})
    for id,y in [('STATUS',464),('WORKER',486),('INPUTS',508),('HINT',530)]:properties('MD_BARRACKS_'+id,{'Y':y,'Height':18})
    if 'MD_BARRACKS_SABOTEUR' not in block:
        block+='\n\t\t\t\tButton@MD_BARRACKS_SABOTEUR:\n\t\t\t\t\tX: 9\n\t\t\t\t\tY: 432\n\t\t\t\t\tWidth: 258\n\t\t\t\t\tHeight: 26\n\t\t\t\t\tFont: TinyBold\n\t\t\t\t\tBackground: md-button\n\t\t\t\t\tTooltipContainer: TOOLTIP_CONTAINER\n\t\t\t\t\tTooltipTemplate: BUTTON_WITH_DESC_HIGHLIGHT_TOOLTIP\n'
    # Blank lines at column zero terminate the parent in MiniYaml's update parser.
    text=text[:start]+block.rstrip()+text[end:]
    lines=['\t\tBackground@MD_SABOTEUR_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 286','\t\t\tBackground: md-panel','\t\t\tChildren:']
    for kind,id,y,h in [('Label','TITLE',8,20),('Label','HINT',32,40),('Button','ACTION_0',78,29),('Button','ACTION_1',113,29),('Button','ACTION_2',148,29),('Button','STOP',183,29),('Label','STATUS',222,54)]:
        lines += [f'\t\t\t\t{kind}@MD_SABOTEUR_{id}:','\t\t\t\t\tX: 9',f'\t\t\t\t\tY: {y}','\t\t\t\t\tWidth: 392',f'\t\t\t\t\tHeight: {h}','\t\t\t\t\tFont: TinyBold']
        if kind=='Button':lines+=['\t\t\t\t\tBackground: md-button']
        else:lines+=['\t\t\t\t\tWordWrap: True','\t\t\t\t\tVAlign: Top']
    return text.rstrip()+'\n'+'\n'.join(lines)+'\n'


def wire(text,package='medieval'):
    for anchor,entry in [('herds.yaml','saboteur.yaml'),('sequences/livestock-visuals.yaml','sequences/saboteur.yaml'),('weapons.yaml','saboteur-weapons.yaml'),('fluent/herds.ftl','fluent/saboteur.ftl')]:
        text=text.replace('\tmedieval|'+entry+'\n','')
        target='\t'+package+'|'+entry
        if target not in text:
            if '\tmedieval|'+anchor not in text:raise ValueError(anchor)
            text=text.replace('\tmedieval|'+anchor,'\tmedieval|'+anchor+'\n'+target,1)
    return text


def install(staged):
    for file in ('art/saboteur.png','art/sabotage-fire.png','saboteur.yaml','saboteur-weapons.yaml','sequences/saboteur.yaml','fluent/saboteur.ftl','chrome/ingame-player.yaml'):
        if file.endswith('.png'):
            if staged.resolve()!=MOD.resolve():shutil.copy2(staged/file,MOD/file)
        else:(MOD/file).write_text((staged/file).read_text(encoding='utf-8').replace('saboteur-preview|','medieval|'),encoding='utf-8',newline='\n')
    for manifest in MOD.parent.glob('medieval*/mod.yaml'):
        text=manifest.read_text(encoding='utf-8')
        if '$medieval: medieval' in text and 'preview|' not in text:manifest.write_text(wire(text),encoding='utf-8',newline='\n')
    test=MOD.parent/'medieval-saboteur';test.mkdir(exist_ok=True)
    (MOD/'saboteur-tests.yaml').write_text((staged/'saboteur-tests.yaml').read_text(encoding='utf-8'),encoding='utf-8',newline='\n')
    (test/'mod.yaml').write_text(wire((MOD/'mod.yaml').read_text(encoding='utf-8')).replace('\tmedieval|saboteur.yaml','\tmedieval|saboteur.yaml\n\tmedieval|saboteur-tests.yaml').replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4'),encoding='utf-8',newline='\n')


def main(output):
    target=output or MOD;package='saboteur-preview' if output else 'medieval'
    for folder in ('art','sequences','fluent','chrome'): (target/folder).mkdir(parents=True,exist_ok=True)
    (target/'saboteur.yaml').write_text(RULES,encoding='utf-8',newline='\n')
    (target/'saboteur-weapons.yaml').write_text(WEAPONS,encoding='utf-8',newline='\n')
    (target/'fluent/saboteur.ftl').write_text('medieval-saboteur = Saboteur\n',encoding='utf-8',newline='\n')
    (target/'chrome/ingame-player.yaml').write_text(chrome((MOD/'chrome/ingame-player.yaml').read_text(encoding='utf-8')),encoding='utf-8',newline='\n')
    wilderness_art.MOD=target;modes=UNIT_MODES+HERD_MODES+('ignite','slaughter')
    wilderness_art.save('saboteur',[unit_frame('saboteur',f,s,m) for m in modes for f in range(8) for s in range(8)])
    wilderness_art.save('sabotage-fire',[fire(s) for s in range(16)])
    seq=['md.saboteur:','\tDefaults:',f'\t\tFilename: {package}|art/saboteur.png','\t\tOffset: 0, -12']
    for i,m in enumerate(modes):seq += [f'\t{m}:',f'\t\tStart: {i*64}','\t\tLength: 8','\t\tFacings: 8','\t\tTick: 120']
    seq += ['','md.sabotage-fire:','\tidle:',f'\t\tFilename: {package}|art/sabotage-fire.png','\t\tOffset: 0, -48','\t\tLength: 16','']
    (target/'sequences/saboteur.yaml').write_text('\n'.join(seq),encoding='utf-8',newline='\n')
    (target/'saboteur-tests.yaml').write_text('World:\n\tMedievalSaboteurValidation:\n\tMedievalWildHerds:\n\t\tSpawnAnimals: False\n\tMedievalDayNight:\n\t\tDayTicks: 20000\n\t\tNightTicks: 3000\n\tMedievalWeather:\n\t\tFirstRainTicks: 30000\n\n^MDWORKER:\n\tMedievalNeedsFood:\n\t\tMealInterval: 30000\n\t\tStarvationDelay: 60000\n\nmd.pasture:\n\tMedievalLivestock:\n\t\tBirthTicks: 15000\n',encoding='utf-8',newline='\n')
    if output:
        text=wire((MOD/'mod.yaml').read_text(encoding='utf-8'),package)
        text=text.replace('\t\t$medieval: medieval',f'\t\t$medieval: medieval\n\t\t^EngineDir|mods/{target.name}: {package}')
        text=text.replace('medieval|chrome/ingame-player.yaml',package+'|chrome/ingame-player.yaml')
        text=text.replace('\t'+package+'|saboteur.yaml','\t'+package+'|saboteur.yaml\n\t'+package+'|saboteur-tests.yaml')
        folder=MOD.parent/'medieval-saboteur-preview';folder.mkdir(exist_ok=True)
        (folder/'mod.yaml').write_text(text.replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4'),encoding='utf-8',newline='\n')
    else:install(MOD)
    out=MOD.parents[1]/'analysis/medieval/saboteur';out.mkdir(parents=True,exist_ok=True)
    preview=Image.new('RGB',(672,232),'#526247');d=ImageDraw.Draw(preview)
    for row,mode in enumerate(('run','ignite','slaughter','lead')):
        d.text((4,row*56),mode,fill='#eee5ce')
        for f in range(8):
            p=unit_frame('saboteur',f,4,mode);preview.paste(p,(100+f*68,row*56),p)
    preview.save(out/'poses.png')
    print('Saboteur content ready: '+str(target))


if __name__=='__main__':
    parser=argparse.ArgumentParser();group=parser.add_mutually_exclusive_group();group.add_argument('--output',type=Path);group.add_argument('--install-from',type=Path);args=parser.parse_args()
    if args.install_from:install(args.install_from)
    else:main(args.output)
