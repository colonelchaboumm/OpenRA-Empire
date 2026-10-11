"""Original procedural foley for the medieval mod (no recordings or music).

Rebuild: python mods/medieval/tools/village_audio.py
16-bit mono PCM, deterministic seeds, fades and headroom on every asset.
"""
import array
import json
import math
from pathlib import Path
import random
import wave

OUT = Path(__file__).resolve().parents[1] / 'audio'
RATE = 22050
TAU = math.tau
REPORT = {}

def buffer(seconds): return [0.0] * round(seconds * RATE)

def add(dst, src, start=0, gain=1):
    at=round(start*RATE)
    for i in range(min(len(src),len(dst)-at)):
        if at+i>=0: dst[at+i]+=src[i]*gain

def noise(seconds, rng, cutoff=.35):
    out=[]; low=0
    for _ in range(round(seconds*RATE)):
        low += cutoff*(rng.uniform(-1,1)-low)
        out.append(low)
    return out

def knock(seconds, rng, metal=False, low=1):
    out=noise(seconds,rng,.55 if metal else .18)
    modes=((587,1),(933,.5),(1433,.28),(2273,.2)) if metal else ((113,1),(187,.48),(367,.16))
    for i in range(len(out)):
        t=i/RATE
        attack=min(1,t/.002)
        out[i]=attack*(out[i]*math.exp(-t*55)*.65+sum(
            amp*math.sin(TAU*f*low*t)*math.exp(-t*(7 if metal else 20)*(1+k*.3))*.3
            for k,(f,amp) in enumerate(modes)))
    return out

def scrape(seconds,rng,roughness=35):
    out=noise(seconds,rng,.28)
    for i in range(len(out)):
        t=i/RATE; e=math.sin(math.pi*t/seconds)**1.4
        out[i]*=e*(.22+.78*abs(math.sin(TAU*roughness*t)))
    return out

def creak(seconds,rng):
    out=noise(seconds,rng,.035); phase=0
    for i in range(len(out)):
        t=i/RATE; phase+=TAU*(185+65*math.sin(t*7)+20*math.sin(t*33))/RATE
        envelope=math.sin(math.pi*t/seconds)**1.2
        out[i]=(out[i]*.4+.13*math.sin(phase)+.05*math.sin(phase*2.07)) * envelope * (.5+.5*math.sin(t*43)**2)
    return out

def pluck(freq, seconds, rng):
    # Karplus-Strong string with a quiet body resonance.
    size=max(2,round(RATE/freq)); ring=[rng.uniform(-.45,.45) for _ in range(size)]
    out=[]; at=0
    for i in range(round(seconds*RATE)):
        v=ring[at]; ring[at]=.497*(v+ring[(at+1)%size]); at=(at+1)%size
        t=i/RATE; out.append((v+.08*math.sin(TAU*freq*t)*math.exp(-t*4))*min(1,t/.004))
    return out

def save(name,samples,peak=.68):
    n=len(samples); fade=min(round(RATE*.014),n//5)
    for i in range(fade):
        samples[i]*=i/fade; samples[-1-i]*=i/fade
    top=max(abs(v) for v in samples) or 1
    factor=peak/top
    pcm=array.array('h',(round(v*factor*32767) for v in samples))
    with wave.open(str(OUT/(name+'.wav')),'wb') as f:
        f.setnchannels(1); f.setsampwidth(2); f.setframerate(RATE); f.writeframes(pcm.tobytes())
    REPORT[name]={'seconds':round(n/RATE,3),'peak':round(max(abs(v) for v in pcm)/32767,4),
                  'rms':round(math.sqrt(sum((v/32767)**2 for v in pcm)/n),4)}

def effect(kind,variant):
    rng=random.Random(sum(map(ord,kind))*17+variant*139)
    if kind=='hoof':
        x=buffer(.28)
        add(x,knock(.12,rng,low=1.5+variant*.13),0,.7)
        add(x,knock(.11,rng,low=1.8),.10,.45)
        add(x,scrape(.2,rng,80),.02,.18)
    elif kind=='step':
        x=scrape(.17,rng,73); add(x,knock(.1,rng,low=.55),gain=.35)
    elif kind in ('axe','hammer','pick','anvil','sword','hit'):
        metal=kind in ('pick','anvil','sword')
        x=knock(.8 if metal else .38,rng,metal,low=rng.uniform(.85,1.2))
        if kind in ('axe','pick'): add(x,scrape(.17,rng,80),.035,.8)
        if kind=='hit': x=[v*.7 for v in x]
    elif kind in ('hoe','harvest','load'):
        x=scrape(.5,rng,17 if kind=='hoe' else 63)
        if kind=='load': add(x,knock(.3,rng,low=.65),.05,.6)
    elif kind=='cart':
        x=creak(.9,rng)
        for t in (0,.21,.48,.72): add(x,knock(.14,rng,low=.72),t,.25)
    elif kind in ('pulley','ram-pull','gate'):
        x=creak(.56 if kind=='ram-pull' else 1.2,rng)
        if kind=='gate': add(x,knock(.25,rng),.9,.45)
    elif kind=='saw':
        x=buffer(1.25)
        for t in (0,.35,.72): add(x,scrape(.4,rng,95),t,.6)
    elif kind=='bow':
        x=pluck(165+rng.randrange(24),.4,rng); add(x,scrape(.16,rng,85),.02,.6)
    elif kind=='trebuchet-release':
        x=creak(.72,rng); add(x,scrape(.46,rng,11),.12,1.7); add(x,knock(.22,rng),.32,.55)
    elif kind=='stone-air': x=scrape(.85,rng,6)
    elif kind in ('ram-hit','stone-impact','collapse','tree-fall'):
        length=1.6 if kind=='tree-fall' else .95 if kind=='ram-hit' else 2.4
        x=buffer(length); t0=1.2 if kind=='tree-fall' else 0
        rumble=noise(length-t0,rng,.016)
        for i in range(len(rumble)): rumble[i]*=math.exp(-i/RATE*3)
        add(x,rumble,t0,3); add(x,knock(min(.55,length-t0),rng,low=.43),t0,1.3)
        if kind=='tree-fall': add(x,creak(1.15,rng),0,.5); add(x,scrape(.8,rng,30),.35,.5)
        for _ in range(12 if kind!='ram-hit' else 7):
            t=t0+rng.uniform(.02,min(.9,length-t0-.12))
            add(x,knock(.16,rng,metal=rng.random()<.25,low=rng.uniform(.8,2.5)),t,rng.uniform(.04,.18))
    elif kind=='wind':
        x=noise(10,rng,.015)
        for i in range(len(x)):
            t=i/RATE; x[i]*=(.45+.3*math.sin(t*.72)+.12*math.sin(t*1.6))*min(1,t/1.4,(10-t)/1.4)
    elif kind=='rain':
        x=noise(8,rng,.65);low=noise(8,rng,.035)
        for i in range(len(x)):
            t=i/RATE;x[i]=(x[i]*.36+low[i]*.22)*(.85+.08*math.sin(t*1.3))
        for _ in range(170):
            at=rng.uniform(.05,7.92);drop=noise(.025,rng,.5)
            for i in range(len(drop)):drop[i]*=math.exp(-i/RATE*180)
            add(x,drop,at,rng.uniform(.04,.14))
    elif kind=='bird':
        x=buffer(1.4); f0=1700+variant*320
        for start, dur in ((.03,.14),(.25,.18),(.66,.12),(.9,.22)):
            a=buffer(dur); phase=0
            for i in range(len(a)):
                t=i/RATE; phase+=TAU*(f0+700*math.sin(t/dur*math.pi)+140*math.sin(t*95))/RATE
                a[i]=(math.sin(phase)+.2*math.sin(phase*2))*math.sin(math.pi*t/dur)**1.6
            add(x,a,start,.2)
    elif kind in ('cattle','calf','sheep','lamb'):
        sheep=kind in ('sheep','lamb'); young=kind in ('calf','lamb')
        seconds=(1.25 if sheep else 2.3)*(1+variant*.09)
        x=noise(seconds,rng,.08); phase=0
        base=(180 if sheep else 86)*(1.4 if young else 1)*(1+variant*.08)
        for i in range(len(x)):
            t=i/RATE; f=base*(1+.12*math.sin(t/seconds*math.pi)+(.055 if sheep else .018)*math.sin(t*(67 if sheep else 23)))
            phase+=TAU*f/RATE
            formants=sum(math.sin(phase*k)*(.22*math.exp(-((k*f-(780 if sheep else 490))/250)**2)
                +.12*math.exp(-((k*f-(1550 if sheep else 1050))/340)**2)+.07/k) for k in range(1,18))
            tremolo=.65+.35*math.sin(t*67)**2 if sheep else 1
            x[i]=(formants+x[i]*.12)*math.sin(math.pi*t/seconds)**1.6*tremolo
    elif kind=='forge':
        x=buffer(2.2)
        for at in (.0,.9):
            puff=noise(.85,rng,.22)
            for i in range(len(puff)): puff[i]*=math.sin(math.pi*i/len(puff))**2
            add(x,puff,at,.45);add(x,creak(.45,rng),at,.13)
        add(x,knock(.7,rng,True,low=.72+variant*.07),1.35,.3)
    elif kind=='night':
        x=buffer(9.6)
        # Irregular insect pairs, quiet leaf movement and generous gaps.
        add(x,noise(9.6,rng,.012),gain=.12)
        for _ in range(26):
            at=rng.uniform(.1,8.9); frequency=rng.uniform(2900,4200)
            chirp=buffer(.24)
            for i in range(len(chirp)):
                t=i/RATE
                chirp[i]=math.sin(TAU*frequency*t)*math.sin(math.pi*t/.24)**2*(.5+.5*math.sin(TAU*31*t))**4
            add(x,chirp,at,rng.uniform(.03,.09))
        for i in range(len(x)): x[i]*=min(1,i/RATE/.7,(9.6-i/RATE)/.7)
    elif kind=='owl':
        x=buffer(2.7)
        for at,duration,gain in ((.1,.32,.45),(.72,.85,.65)):
            hoot=buffer(duration); phase=0
            for i in range(len(hoot)):
                t=i/RATE;phase+=TAU*(370+variant*45-40*t/duration+5*math.sin(t*28))/RATE
                hoot[i]=(math.sin(phase)+.14*math.sin(phase*2))*math.sin(math.pi*t/duration)**1.5
            add(x,hoot,at,gain)
    elif kind=='drum-group':
        x=buffer(.93)
        for at,gain in ((0,.7),(.47,.5)):
            for offset,pitch,level in ((0,1,1),(.025,.88,.42),(.044,1.13,.32)):
                beat=buffer(.36)
                for i in range(len(beat)):
                    t=i/RATE;envelope=math.exp(-t*15)*min(1,t*1200)
                    skin=math.sin(TAU*(105*pitch*t+4*(1-math.exp(-t*35))))
                    skin+=.24*math.sin(TAU*184*pitch*t)*math.exp(-t*10)
                    beat[i]=(skin+rng.uniform(-1,1)*math.exp(-t*65)*.5)*envelope
                add(x,beat,at+offset+variant*.007,gain*level)
    elif kind in ('market','fire'):
        seconds=3.4; x=noise(seconds,rng,.04)
        x=[v*.15 for v in x]
        for _ in range(18 if kind=='fire' else 6):
            add(x,knock(.09,rng,metal=kind=='market',low=rng.uniform(.9,2.6)),rng.uniform(0,seconds-.2),rng.uniform(.02,.12))
        if kind=='market': add(x,scrape(.7,rng,13),.5,.25)
    elif kind in ('complete','ready','select','order'):
        x=buffer(1.3 if kind=='complete' else .6)
        notes=[(0,587),(.14,740),(.28,880)] if kind=='complete' else [(0,294),(.1,440)] if kind=='ready' else [(0,330 if kind=='select' else 392)]
        for start,f in notes: add(x,pluck(f,.8,rng),start,.45)
    else: raise ValueError(kind)
    return x

def main():
    OUT.mkdir(exist_ok=True)
    variants={'step':3,'axe':3,'hammer':2,'pick':3,'anvil':2,'sword':3,'hit':2,'hoe':2,'harvest':2,'load':2,
              'cart':2,'pulley':2,'ram-pull':1,'gate':1,'saw':2,'bow':2,'trebuchet-release':1,'stone-air':1,
              'ram-hit':2,'stone-impact':2,'collapse':1,'tree-fall':1,'wind':1,'bird':3,'cattle':2,'market':1,'fire':1,
              'complete':1,'ready':1,'select':1,'order':1,'rain':1,
              'sheep':3,'calf':2,'lamb':2,'hoof':3,'forge':2,'night':2,'owl':2,'drum-group':2}
    for kind,count in variants.items():
        for v in range(count): save(f'{kind}-{v+1}',effect(kind,v),.35 if kind in ('wind','market','fire','select','order','rain','night') else .7)
    (OUT/'manifest.json').write_text(json.dumps({'source':'Original procedural synthesis; village_audio.py', 'sampleRate':RATE,'channels':1,'bits':16,'assets':REPORT},indent=2),encoding='utf-8')
    print(f'Generated {len(REPORT)} original WAV assets in {OUT}')

if __name__=='__main__': main()
