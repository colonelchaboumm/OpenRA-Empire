"""Original deterministic snow foley and layered thunder, no music or recordings."""
import json
import math
import random
from village_audio import RATE, OUT, REPORT, add, buffer, noise, save, creak


def crunch(seed, seconds=.38):
    rng=random.Random(seed)
    x=buffer(seconds)
    powder=noise(seconds,rng,.42)
    low=noise(seconds,rng,.025)
    for i in range(len(x)):
        t=i/RATE
        envelope=(1-math.exp(-t*85))*math.exp(-t*12)
        x[i]=(powder[i]*.55+low[i]*1.3)*envelope
    # Small brittle fractures stagger over the weight transfer of one step.
    for _ in range(46):
        at=rng.uniform(.025,seconds*.64)
        grain=noise(rng.uniform(.006,.02),rng,.65)
        grain=[v*math.exp(-j/RATE*230) for j,v in enumerate(grain)]
        add(x,grain,at,rng.uniform(.18,.7))
    return x


def thunder(seed, seconds):
    rng=random.Random(seed)
    low=noise(seconds,rng,.006); middle=noise(seconds,rng,.043); high=noise(seconds,rng,.5)
    result=[]
    for i in range(len(low)):
        t=i/RATE
        attack=min(1,t/.024)
        # Crack followed by uneven low rolling echoes, not a periodic tone.
        roll=sum(a*math.exp(-max(0,t-delay)/decay)*min(1,max(0,t-delay)*18)
                 for delay,a,decay in ((0,1,1.3),(.43,.55,1.8),(1.27,.65,1.3),(2.61,.38,1.1)))
        tail=min(1,(seconds-t)/.9)
        result.append((low[i]*9*roll+middle[i]*1.3*roll+high[i]*math.exp(-t*16)*.6)*attack*tail)
    return result


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    for v in range(1,5): save(f'snow-step-{v}',crunch(817+v*19,.32+v*.025),.63)
    for v in range(1,3):
        x=buffer(1.1)
        add(x,creak(1.1,random.Random(482+v)),gain=.12)
        for j,t in enumerate((0,.20,.43,.69)): add(x,crunch(101+v*17+j),t,.6)
        save(f'snow-cart-{v}',x,.62)
    for v in range(1,4): save(f'thunder-{v}',thunder(952+v*131,5.5+v*.4),.78)
    assets={k:v for k,v in REPORT.items() if k.startswith(('snow-step-', 'snow-cart-', 'thunder-'))}
    (OUT/'weather-manifest.json').write_text(json.dumps(dict(source='Original procedural effects: tools/weather_audio.py',sampleRate=RATE,channels=1,bits=16,assets=assets),indent=2),encoding='utf-8')
    print(f'Generated {len(assets)} snow and thunder effects.')


if __name__=='__main__': main()
