"""Keep wildlife and showers available in all medieval maps and test manifests."""
from pathlib import Path
MOD=Path(__file__).resolve().parents[1]

def main():
    for path in MOD.parent.glob('medieval*/mod.yaml'):
        text=path.read_text(encoding='utf-8')
        for anchor,extra in (('heraldry.yaml','wilderness.yaml'),('sequences/heraldry.yaml','sequences/wilderness.yaml')):
            entry='\tmedieval|'+extra
            if entry not in text:text=text.replace('\tmedieval|'+anchor,'\tmedieval|'+anchor+'\n'+entry,1)
        path.write_text(text,encoding='utf-8',newline='\n')
    test=MOD.parent/'medieval-wilderness';test.mkdir(exist_ok=True)
    text=(MOD/'mod.yaml').read_text(encoding='utf-8').replace('\tmedieval|wilderness.yaml','\tmedieval|wilderness.yaml\n\tmedieval|wilderness-tests.yaml',1)
    text=text.replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4')
    (test/'mod.yaml').write_text(text,encoding='utf-8',newline='\n')

if __name__=='__main__':main()
