"""Native medieval menus and reproducible UI panels; keep the generated artwork intact."""
from pathlib import Path
import json
import re
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[3]
MOD = ROOT/'mods/medieval'


def panels():
    # Small authored nine-patches, not an alteration of the generated illustration.
    out = Image.new('RGBA', (512, 64))
    themes = [('#283a32','#887044'),('#3d5345','#d4b879'),('#16271f','#b59a64'),
              ('#232924','#575a4a'),('#51452c','#dbc184'),('#171e1b','#897449'),
              ('#101a16','#b4955b'),('#223027','#49533f')]
    d = ImageDraw.Draw(out)
    for i,(fill,border) in enumerate(themes):
        x=i*64
        d.rectangle((x,0,x+63,63),fill=fill)
        d.rectangle((x,0,x+63,63),outline='#101812')
        d.rectangle((x+1,1,x+62,62),outline=border)
        d.line((x+3,3,x+60,3),fill='#736748' if i!=3 else '#424a3f')
        d.line((x+3,60,x+60,60),fill='#111b15')
        for px,py in ((x+2,2),(x+60,2),(x+2,60),(x+60,60)):
            d.rectangle((px,py,px+1,py+1),fill=border)
    out.save(MOD/'art/menu/panels.png')
    names={}
    for prefix in ('button','md-button','checkbox','sidebar-button-allies','sidebar-button-soviet','sidebar-button-observer','command-button-allies','command-button-soviet'):
        for suffix,state in (('',0),('-hover',1),('-pressed',2),('-disabled',3),('-highlighted',4),('-highlighted-hover',1),('-highlighted-pressed',2),('-highlighted-disabled',3)):
            names[prefix+suffix]=state
    names.update({'dialog':5,'dialog2':0,'dialog3':2,'dialog4':5,'dialog5':5,'md-panel':5,'md-menu-panel':6,
                  'textfield':2,'textfield-focused':4,'textfield-hover':1,'textfield-disabled':3,
                  'scrollpanel-bg':7,'scrollpanel-button':0,'scrollpanel-button-hover':1,'scrollpanel-button-pressed':2,
                  'scrollpanel-button-disabled':3,'scrollitem-hover':0,'scrollitem-pressed':2,'scrollitem-highlighted':4,
                  'scrollheader':2,'scrollheader-highlighted':4,'progressbar-bg':7,'progressbar-thumb':4})
    chrome=''
    for name,state in names.items():
        chrome+=f'{name}:\n\tImage: medieval|art/menu/panels.png\n\tPanelRegion: {state*64}, 0, 4, 4, 56, 56, 4, 4\n\n'
    # The full illustration is uploaded once by MedievalLoadScreen into a POT sheet.
    (MOD/'frontend-chrome.yaml').write_text(chrome.rstrip()+'\n',encoding='utf-8',newline='\n')


def item(kind,name,depth=0,**fields):
    t='\t'*depth
    return f'{t}{kind}@{name}:\n'+''.join(f'{t}\t{k}:'+(' '+str(v) if v!='' else '')+'\n' for k,v in fields.items())


def menus():
    text=item('Container','MAINMENU',Logic='MedievalMainMenuLogic',Width='WINDOW_WIDTH',Height='WINDOW_HEIGHT',Children='')
    text+=item('MedievalBackdrop','MD_BACKDROP',2,Width='WINDOW_WIDTH',Height='WINDOW_HEIGHT')
    text+=item('LogicKeyListener','GLOBAL_KEYHANDLER',2,Logic='ScreenshotHotkeyLogic, MuteHotkeyLogic')
    text+='\t\t\t\tTakeScreenshotKey: TakeScreenshot\n\t\t\t\tMuteAudioKey: ToggleMute\n'
    text+=item('MedievalTitle','MD_TITLE',2,X='WINDOW_WIDTH / 16',Y='(WINDOW_HEIGHT - 560) / 2',Width=320,Height=168)
    text+=item('Container','MENUS',2,X='WINDOW_WIDTH / 16',Y='(WINDOW_HEIGHT - 560) / 2 + 198',Width=310,Height=330,Children='')
    groups={
        'MAIN_MENU':('md-menu-welcome',[('SINGLEPLAYER_BUTTON','md-menu-solo'),('MULTIPLAYER_BUTTON','md-menu-multi'),('CONTENT_BUTTON','md-menu-guide'),('SETTINGS_BUTTON','md-menu-settings'),('EXTRAS_BUTTON','md-menu-extras'),('QUIT_BUTTON','md-menu-quit')]),
        'SINGLEPLAYER_MENU':('md-menu-solo',[('MISSIONS_BUTTON','md-menu-quick'),('SKIRMISH_BUTTON','md-menu-choose'),('LOAD_BUTTON','md-menu-load'),('BACK_BUTTON','md-menu-back')]),
        'EXTRAS_MENU':('md-menu-extras',[('REPLAYS_BUTTON','md-menu-replays'),('MAP_EDITOR_BUTTON','md-menu-editor'),('ASSETBROWSER_BUTTON','md-menu-assets'),('CREDITS_BUTTON','md-menu-credits'),('BACK_BUTTON','md-menu-back'),('MUSIC_BUTTON','md-menu-hidden-music')]),
        'MAP_EDITOR_MENU':('md-menu-editor',[('NEW_MAP_BUTTON','md-menu-new-map'),('LOAD_MAP_BUTTON','md-menu-open-map'),('BACK_BUTTON','md-menu-back')])}
    for name,(heading,buttons) in groups.items():
        text+=item('Background',name,4,Width='PARENT_WIDTH',Height='PARENT_HEIGHT',Background='md-menu-panel',Children='')
        text+=item('Label',name+'_TITLE',6,X=16,Y=13,Width=278,Height=26,Text=heading,Font='Bold',TextColor='DAC08A')
        for index,(id,label) in enumerate(buttons):
            fields=dict(X=16,Y=48+index*44,Width=278,Height=34,Text=label,Font='Bold')
            if id=='BACK_BUTTON': fields['Key']='escape'
            if id=='MUSIC_BUTTON':fields['Visible']='False'
            text+=item('Button',id,6,**fields)
    text+=item('Label','MD_FOOTER',2,X='WINDOW_WIDTH / 16',Y='WINDOW_HEIGHT - 34',Width=500,Height=20,Text='md-menu-footer',Font='Tiny',TextColor='C3B694',Shadow='True')
    (MOD/'chrome/mainmenu.yaml').write_text(text,encoding='utf-8',newline='\n')

    pause=(ROOT/'mods/common/chrome/ingame-menu.yaml').read_text(encoding='utf-8')
    pause=pause.replace('SETTINGS, MUSIC,','SETTINGS,')
    pause=re.sub(r'\t\t(?:Image@LOGO|Label@VERSION_LABEL|Background@BORDER):\n(?:\t{3,}[^\n]*\n|\n)*','',pause)
    pause=pause.replace('Width: 200','Width: 260').replace('Width: 140','Width: 228')
    pause=pause.replace('13 + (WINDOW_WIDTH - 522) / 4 - WIDTH / 2','26')
    pause=pause.replace('label-menu-buttons-title','md-menu-pause')
    pause=pause.replace('Logic: IngameMenuLogic', 'Logic: IngameMenuLogic, MedievalPauseOverviewLogic',1)
    pause=pause.replace('\t\tContainer@PANEL_ROOT:\n','')
    pause+=item('Background','MD_PAUSE_OVERVIEW',2,X='WINDOW_WIDTH - 620',Y='(WINDOW_HEIGHT - HEIGHT) / 2',Width=580,Height=420,Children='')
    pause+=item('Label','MD_PAUSE_HEADING',4,X=26,Y=20,Width=528,Height=32,Font='BigBold',Text='md-pause-domain',TextColor='DAC08A')
    pause+=item('Label','MD_PAUSE_REGION',4,X=26,Y=65,Width=528,Height=30,Font='Bold',WordWrap='True')
    pause+=item('Label','MD_PAUSE_STATUS',4,X=26,Y=125,Width=528,Height=255,Font='Regular',WordWrap='True',VAlign='Top')
    (MOD/'chrome/ingame-menu.yaml').write_text(pause,encoding='utf-8',newline='\n')

    guide=item('Background','MD_GUIDE',Logic='MedievalGuideLogic',X='(WINDOW_WIDTH - WIDTH) / 2',Y='(WINDOW_HEIGHT - HEIGHT) / 2',Width=900,Height=550,Children='')
    guide+=item('Label','GUIDE_TITLE',2,X=26,Y=20,Width=840,Height=30,Font='BigBold',Text='md-menu-guide',TextColor='DAC08A')
    for i in range(5):guide+=item('Button',f'GUIDE_TAB_{i}',2,X=26+i*170,Y=70,Width=164,Height=34,Text=f'md-guide-tab-{i}',Font='Bold')
    guide+=item('Label','GUIDE_HEADING',2,X=30,Y=130,Width=830,Height=28,Font='MediumBold',TextColor='DAC08A')
    guide+=item('Label','GUIDE_TEXT',2,X=30,Y=182,Width=830,Height=285,Font='Regular',WordWrap='True',VAlign='Top')
    guide+=item('Button','GUIDE_CLOSE',2,X=694,Y=490,Width=176,Height=34,Font='Bold',Text='md-menu-back',Key='escape')
    (MOD/'chrome/guide.yaml').write_text(guide,encoding='utf-8',newline='\n')

    lobby=(ROOT/'mods/common/chrome/lobby.yaml').read_text(encoding='utf-8')
    lobby=lobby.replace('Logic: LobbyLogic', 'Logic: LobbyLogic, MedievalLobbyPresentationLogic',1)
    (MOD/'chrome/lobby.yaml').write_text(lobby,encoding='utf-8',newline='\n')
    audio=(ROOT/'mods/common/chrome/settings-audio.yaml').read_text(encoding='utf-8')
    audio=audio.replace('Logic: AudioSettingsLogic', 'Logic: AudioSettingsLogic, MedievalAudioPresentationLogic',1)
    (MOD/'chrome/settings-audio.yaml').write_text(audio,encoding='utf-8',newline='\n')
    saves=(ROOT/'mods/common/chrome/gamesave-browser.yaml').read_text(encoding='utf-8')
    saves=saves.replace('Width: 100','Width: 120').replace('X: 130','X: 170').replace('X: 240','X: 300').replace('PARENT_WIDTH - WIDTH - 130','PARENT_WIDTH - WIDTH - 150')
    saves=saves.replace('Button@DELETE_ALL_BUTTON:\n\t\t\tX: 20\n\t\t\tY: PARENT_HEIGHT - 45\n\t\t\tWidth: 120','Button@DELETE_ALL_BUTTON:\n\t\t\tX: 20\n\t\t\tY: PARENT_HEIGHT - 45\n\t\t\tWidth: 140')
    (MOD/'chrome/gamesave-browser.yaml').write_text(saves,encoding='utf-8',newline='\n')
    ingame=MOD/'chrome/ingame-player.yaml'
    text=ingame.read_text(encoding='utf-8')
    if 'MedievalStartCameraLogic' not in text:
        text=re.sub(r'^(\tLogic:.*)$',r'\1, MedievalStartCameraLogic',text,count=1,flags=re.M)
    ingame.write_text(text,encoding='utf-8',newline='\n')


def wire():
    p=MOD/'mod.yaml'; text=p.read_text(encoding='utf-8')
    text=re.sub(r'LoadScreen:.*?(?=\n\S)', 'LoadScreen: MedievalLoadScreen\n\tImage: medieval|art/menu/bourg-dusk.png\n',text,flags=re.S)
    for before,after in [('common|chrome/mainmenu.yaml','medieval|chrome/mainmenu.yaml'),('common|chrome/ingame-menu.yaml','medieval|chrome/ingame-menu.yaml'),
                         ('common|chrome/lobby.yaml','medieval|chrome/lobby.yaml'),
                         ('common|chrome/settings-audio.yaml','medieval|chrome/settings-audio.yaml'),
                         ('common|chrome/gamesave-browser.yaml','medieval|chrome/gamesave-browser.yaml'),
                         ('common|fluent/common.ftl','medieval|fluent/common-fr.ftl'),('common|fluent/chrome.ftl','medieval|fluent/chrome-fr.ftl')]:
        text=text.replace(before,after)
    for anchor,value in [('\tmedieval|chrome.yaml','\tmedieval|frontend-chrome.yaml'),('\tmedieval|chrome/mainmenu.yaml','\tmedieval|chrome/guide.yaml'),
                         ('\tmedieval|fluent/medieval.ftl','\tmedieval|fluent/frontend.ftl'),('\tra|metrics.yaml','\tmedieval|frontend-metrics.yaml')]:
        if value+'\n' not in text:text=text.replace(anchor+'\n',anchor+'\n'+value+'\n',1)
    if '\tMedievalTitle:' not in text:
        text=text.replace('Fonts:\n','Fonts:\n\tMedievalTitle:\n\t\tFont: common|FreeSansBold.ttf\n\t\tSize: 38\n\t\tAscender: 39\n',1)
    # This engine exposes a fixed FluentCulture field, but cannot load it from YAML.
    text=re.sub(r'^FluentCulture:.*\n\n?', '', text, flags=re.M)
    p.write_text(text,encoding='utf-8',newline='\n')
    (MOD/'frontend-metrics.yaml').write_text('''Metrics:
\tTextColor: EEE5CE
\tButtonTextColor: F4EAD2
\tButtonTextColorDisabled: 939587
\tTextDisabledColor: 939587
\tTextHighlightColor: E4C486
\tTextfieldColor: F4EAD2
\tTextfieldColorHighlight: 5B5138
''',encoding='utf-8',newline='\n')


def localize():
    changes=json.loads((MOD/'tools/frontend_translations.json').read_text(encoding='utf-8'))
    for name in ('common','chrome'):
        text=(ROOT/f'mods/common/fluent/{name}.ftl').read_text(encoding='utf-8')
        for key,value in changes.items():
            if '.' in key:
                root,attribute=key.split('.',1)
                pattern=rf'(^'+re.escape(root)+r' =[^\n]*\n)((?:[ \t][^\n]*\n|\n)*)'
                def replace(m):
                    return m[1]+re.sub(r'^(    \.'+re.escape(attribute)+r' =).*$',lambda a:a[1]+' '+value,m[2],flags=re.M)
                text=re.sub(pattern,replace,text,flags=re.M)
            else:text=re.sub(r'^'+re.escape(key)+r' =[^\n]*$',lambda m:key+' = '+value,text,flags=re.M)
        (MOD/f'fluent/{name}-fr.ftl').write_text(text,encoding='utf-8',newline='\n')


def main():
    panels();menus();localize();wire()
    print('Medieval front end generated; original illustration preserved.')


if __name__=='__main__':main()
