"""Extend the native animated priest and synthesize original bronze-bell/ore foley.

No music or third-party recordings. Rebuild only the assets belonging to this feature.
"""
import json
import math
import random
from priest_animation import priest_heal_frame, HEAL_FRAMES
from wilderness_art import save as sprite, MOD
from village_audio import RATE, REPORT, buffer, add, knock, noise, save


def main():
    sprite('priest-heal', [priest_heal_frame(facing, step)
                           for facing in range(8) for step in range(HEAL_FRAMES)])
    for variant in (1, 2):
        rng = random.Random(9300 + variant)
        bell = buffer(5.8)
        # Inharmonic bronze partials: hum, prime, tierce, quint and nominal.
        # Slight beating and faster damping of the high modes avoid a pure sine tone.
        modes = [(0.5, .40, .55), (1, 1, .8), (1.19, .5, 1.1),
                 (1.5, .27, 1.4), (2, .5, 1.3), (2.76, .16, 2.2), (3.94, .1, 3.4)]
        hit = noise(.13, rng, .45)
        for i in range(len(bell)):
            t = i / RATE
            bell[i] = min(1, t / .006) * sum(
                amp * math.exp(-t * decay) * (
                    math.sin(math.tau * 220 * ratio * t)
                    + .18 * math.sin(math.tau * (220 * ratio + .6 + variant * .12) * t))
                for ratio, amp, decay in modes)
            if i < len(hit): bell[i] += hit[i] * .24 * math.exp(-t * 42)
        save(f'church-bell-{variant}', bell, peak=.73)
        ore = buffer(1.15)
        for n in range(8):
            start = .04 + n * .115 + rng.uniform(0, .03)
            add(ore, knock(.24, rng, metal=n % 3 == 0, low=rng.uniform(1.5, 2.6)),
                start, rng.uniform(.20, .42))
        save(f'gold-sort-{variant}', ore, peak=.52)
    (MOD/'audio/church-gold-manifest.json').write_text(json.dumps({
        'source': 'Original procedural synthesis; church_gold_animation.py',
        'sampleRate': RATE, 'channels': 1, 'bits': 16, 'assets': REPORT}, indent=2), encoding='utf-8')
    print('Created 128 directional healing frames and 4 original audio effects.')


if __name__ == '__main__': main()
