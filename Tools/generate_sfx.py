#!/usr/bin/env python3
"""Procedurally synthesizes the game's sound effects as 16-bit mono WAVs.

Pure stdlib. Output: Assets/Resources/Audio/Sfx/. Deterministic (seeded).
Recipe per sound: a pitch-dropping sine body + lowpass-filtered noise burst +
short click transient, soft-clipped for weight. Tweak the parameter dicts and
rerun; Unity hot-reloads the clips.
"""
import math, random, struct, wave, os

SR = 44100
OUT_DIR = os.path.join(os.path.dirname(__file__), "..",
                       "Assets", "Resources", "Audio", "Sfx")


def synth_impact(seed, duration, f_start, f_end, body_amp, noise_amp, noise_cutoff,
                 body_tau, noise_tau, click_amp=0.0, drive=1.0, attack=0.008,
                 warmth=0.15, peak_level=0.6):
    rng = random.Random(seed)
    n = int(SR * duration)
    samples = []
    phase = 0.0
    lp = 0.0  # one-pole lowpass state
    lp_k = min(1.0, 2.0 * math.pi * noise_cutoff / SR)

    for i in range(n):
        t = i / SR
        # exponential pitch drop reads as weight
        f = f_start * (f_end / f_start) ** (t / duration)
        phase += 2.0 * math.pi * f / SR
        # fundamental + a touch of second harmonic for warmth (not edge)
        tone = math.sin(phase) + warmth * math.sin(2.0 * phase)
        body = tone * body_amp * math.exp(-t / body_tau)

        noise = (rng.random() * 2.0 - 1.0)
        lp += lp_k * (noise - lp)
        thump_noise = lp * noise_amp * math.exp(-t / noise_tau)

        click = (rng.random() * 2.0 - 1.0) * click_amp * max(0.0, 1.0 - t / 0.006)

        # soft attack ramp: an instant start reads as a harsh edge
        env = min(1.0, t / attack) if attack > 0 else 1.0
        s = math.tanh((body + thump_noise + click) * env * drive)
        samples.append(s)

    # normalize to a consistent (configurable) peak - subtle sounds stay quiet
    peak = max(abs(s) for s in samples) or 1.0
    return [s / peak * peak_level for s in samples]


def synth_swoosh(seed, duration, cutoff_start, cutoff_end, band_ratio=0.35,
                 swell=0.35, gain=3.0, peak_level=0.5):
    """Band-swept noise 'whoosh' (the nudge dash). Two one-pole lowpasses make a
    crude bandpass whose center falls over the sound; a swell-then-die envelope
    (peaking `swell` of the way through) reads as air being pushed aside."""
    rng = random.Random(seed)
    n = int(SR * duration)
    lp_hi = lp_lo = 0.0
    samples = []
    for i in range(n):
        u = (i / SR) / duration
        cutoff = cutoff_start * (cutoff_end / cutoff_start) ** u
        k_hi = min(1.0, 2.0 * math.pi * cutoff / SR)
        k_lo = min(1.0, 2.0 * math.pi * cutoff * band_ratio / SR)
        noise = rng.random() * 2.0 - 1.0
        lp_hi += k_hi * (noise - lp_hi)
        lp_lo += k_lo * (noise - lp_lo)
        env = u / swell if u < swell else 1.0 - (u - swell) / (1.0 - swell)
        env = env * env * (3.0 - 2.0 * env)  # smoothstep: no clicky corners
        samples.append(math.tanh((lp_hi - lp_lo) * gain) * env)

    peak = max(abs(s) for s in samples) or 1.0
    return [s / peak * peak_level for s in samples]


def synth_gun_cock(seed, duration=0.34, gap=0.115, peak_level=0.7):
    """Single gun cock: two mechanical stages - the slide pulled back (bright metallic
    CLICK) then slammed home (deeper CLACK with body). Each stage = a sharp noise
    transient + damped metallic ring partials; a faint slide-scrape connects them."""
    rng = random.Random(seed)
    n = int(SR * duration)
    samples = [0.0] * n

    def click(t0, length, rings, noise_amp, noise_tau, body=0.0):
        start = int(t0 * SR)
        for i in range(start, min(n, start + int(length * SR))):
            t = (i - start) / SR
            v = (rng.random() * 2.0 - 1.0) * noise_amp * math.exp(-t / noise_tau)
            for freq, amp, tau in rings:           # damped metal partials
                v += amp * math.sin(2.0 * math.pi * freq * t) * math.exp(-t / tau)
            if body > 0.0:                         # low mechanical thump
                v += body * math.sin(2.0 * math.pi * 130.0 * t) * math.exp(-t / 0.025)
            samples[i] += v * min(1.0, t / 0.0012)  # hair of attack, no digital edge

    # stage 1: pull back - bright, thin, high metal
    click(0.0, 0.07, rings=((2600, 0.30, 0.010), (3900, 0.18, 0.007), (1700, 0.16, 0.014)),
          noise_amp=0.8, noise_tau=0.006)
    # slide scrape between the stages - quiet filtered rattle
    scr0, scr1 = int(0.03 * SR), int(gap * SR)
    lp = 0.0
    for i in range(scr0, min(n, scr1)):
        t = (i - scr0) / SR
        noise = rng.random() * 2.0 - 1.0
        lp += 0.25 * (noise - lp)
        u = (i - scr0) / max(1, scr1 - scr0)
        samples[i] += (noise - lp) * 0.10 * math.sin(u * math.pi)
    # stage 2: slam home - deeper, heavier, with a thump
    click(gap, 0.16, rings=((1250, 0.34, 0.016), (820, 0.26, 0.022), (2100, 0.16, 0.009)),
          noise_amp=1.0, noise_tau=0.009, body=0.5)

    out = [math.tanh(v * 1.4) for v in samples]
    peak = max(abs(v) for v in out) or 1.0
    return [v / peak * peak_level for v in out]


def write_wav(path, samples):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(
            struct.pack("<h", int(max(-1.0, min(1.0, s)) * 32767)) for s in samples))
    print(path)


SOUNDS = {
    # flick-drop landings: subtle satisfying "felt mallet" thud - near-pure deep sine,
    # soft attack, barely-there noise, no click, no saturation, quiet peak
    # the "round 2" recipe (deep but with real noise body) - picked after A/B rounds;
    # reads as present without sting, and sits well under background music
    "impact_heavy_01": dict(seed="heavy1", duration=0.34, f_start=70, f_end=32,
                            body_amp=1.0, noise_amp=0.4, noise_cutoff=420,
                            body_tau=0.11, noise_tau=0.045, click_amp=0.08,
                            drive=1.1, attack=0.0, warmth=0.0, peak_level=0.85),
    "impact_heavy_02": dict(seed="heavy2", duration=0.30, f_start=62, f_end=30,
                            body_amp=1.0, noise_amp=0.45, noise_cutoff=360,
                            body_tau=0.10, noise_tau=0.040, click_amp=0.06,
                            drive=1.1, attack=0.0, warmth=0.0, peak_level=0.85),
    # the quiet dull thud: the Bullet's wasted-shot feedback (BulletImpact); must
    # stay clearly softer/duller than impact_shatter_01 so "no effect" reads by ear
    "impact_soft_01": dict(seed="soft1", duration=0.16, f_start=95, f_end=55,
                           body_amp=0.8, noise_amp=0.1, noise_cutoff=350,
                           body_tau=0.05, noise_tau=0.025, attack=0.008, peak_level=0.45),
    # support island materializing (laser-wave reveal): a friendly RISING blip -
    # f_end > f_start flips the usual pitch drop into a bubbly "pop"
    "pop_01": dict(seed="pop1", duration=0.11, f_start=150, f_end=330,
                   body_amp=1.0, noise_amp=0.04, noise_cutoff=900,
                   body_tau=0.05, noise_tau=0.02, attack=0.004, peak_level=0.5),
    # bullet impact: a sharp stone CRACK - bright, short, with real bite; reads as
    # "something broke", clearly apart from the soft landing thumps
    "impact_shatter_01": dict(seed="shatter1", duration=0.18, f_start=320, f_end=90,
                              body_amp=0.7, noise_amp=0.85, noise_cutoff=2600,
                              body_tau=0.03, noise_tau=0.035, click_amp=0.35,
                              drive=1.6, attack=0.001, warmth=0.1, peak_level=0.8),
    # failed nudge: a dry knuckle-on-wood KNOCK - higher and shorter than the landing
    # thumps so the ear learns "that was a refusal", with a hard click for the sting
    "nudge_thud_01": dict(seed="nudgethud1", duration=0.13, f_start=210, f_end=80,
                          body_amp=0.9, noise_amp=0.5, noise_cutoff=700,
                          body_tau=0.035, noise_tau=0.02, click_amp=0.25,
                          drive=1.4, attack=0.001, warmth=0.2, peak_level=0.7),
}

MECHANICAL = {
    # bullet transform: a single gun cock (pull back, slam home) - replaced the
    # rising zap after playtest feedback ("awful"); mechanical reads as "weapon ready"
    "gun_cock_01": dict(seed="cock1", duration=0.34, gap=0.115, peak_level=0.7),
}

SWOOSHES = {
    # nudge dash: short airy whoosh - quick swell, falling band, no tonal body
    "swoosh_01": dict(seed="swoosh1", duration=0.20, cutoff_start=2400, cutoff_end=420,
                      swell=0.30, peak_level=0.5),
}

if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, params in SOUNDS.items():
        write_wav(os.path.abspath(os.path.join(OUT_DIR, f"{name}.wav")),
                  synth_impact(**params))
    for name, params in SWOOSHES.items():
        write_wav(os.path.abspath(os.path.join(OUT_DIR, f"{name}.wav")),
                  synth_swoosh(**params))
    for name, params in MECHANICAL.items():
        write_wav(os.path.abspath(os.path.join(OUT_DIR, f"{name}.wav")),
                  synth_gun_cock(**params))
