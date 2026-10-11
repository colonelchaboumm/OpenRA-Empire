"""Original, deterministic two-beat drum sound; no external recording."""
import math
import random
import struct
import wave


def write_drum(path):
    path.parent.mkdir(exist_ok=True)
    rate=22050
    rng=random.Random(73)
    samples=[]
    for frame in range(int(rate*.85)):
        t=frame/rate
        value=0
        for start,gain in ((0,.7),(.48,.5)):
            age=t-start
            if 0<=age<.32:
                envelope=math.exp(-age*15)*min(1,age*1200)
                skin=math.sin(math.tau*(105*age+4*(1-math.exp(-age*35))))
                skin+=.24*math.sin(math.tau*184*age)*math.exp(-age*10)
                attack=rng.uniform(-1,1)*math.exp(-age*65)*.5
                value+=(skin+attack)*envelope*gain
        samples.append(struct.pack('<h',round(max(-1,min(1,value)) * 24000)))
    with wave.open(str(path),'wb') as sound:
        sound.setnchannels(1);sound.setsampwidth(2);sound.setframerate(rate)
        sound.writeframes(b''.join(samples))
