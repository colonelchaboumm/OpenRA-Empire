"""Wire founding assets into the playable mod and its opt-in validation variants."""
from pathlib import Path
MOD=Path(__file__).resolve().parents[1]

def main():
    for path in MOD.parent.glob('medieval*/mod.yaml'):
        text=path.read_text(encoding='utf-8')
        for anchor,extra in (
            ('\tmedieval|siege.yaml','\tmedieval|founding.yaml'),
            ('\tmedieval|sequences/medieval.yaml','\tmedieval|sequences/founding.yaml'),
            ('\tmedieval|fluent/medieval.ftl','\tmedieval|fluent/founding.ftl')):
            if extra not in text:text=text.replace(anchor,anchor+'\n'+extra,1)
        text=text.replace('\n\tmedieval|chrome/founding.yaml','')
        path.write_text(text,encoding='utf-8',newline='\n')
    # Widget roots cannot be merged across files: insert this child once into
    # PLAYER_WIDGETS, including after the general generator rebuilds that file.
    path=MOD/'chrome/ingame-player.yaml';text=path.read_text(encoding='utf-8')
    if 'Background@MD_FOUNDING_PANEL:' not in text:
        text=text.rstrip()+'\n'+(MOD/'chrome/founding.yaml').read_text(encoding='utf-8').split('\tChildren:\n',1)[1]
    text=text.replace('\n\n\t\tBackground@MD_FOUNDING_PANEL:', '\n\t\tBackground@MD_FOUNDING_PANEL:')
    path.write_text(text,encoding='utf-8',newline='\n')

if __name__=='__main__':main()
