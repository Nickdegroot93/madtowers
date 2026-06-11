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
    # normal landings (quieter, shorter - wired up later if it feels right)
    "impact_soft_01": dict(seed="soft1", duration=0.16, f_start=95, f_end=55,
                           body_amp=0.8, noise_amp=0.1, noise_cutoff=350,
                           body_tau=0.05, noise_tau=0.025, attack=0.008, peak_level=0.45),
    # support island materializing (laser-wave reveal): a friendly RISING blip -
    # f_end > f_start flips the usual pitch drop into a bubbly "pop"
    "pop_01": dict(seed="pop1", duration=0.11, f_start=150, f_end=330,
                   body_amp=1.0, noise_amp=0.04, noise_cutoff=900,
                   body_tau=0.05, noise_tau=0.02, attack=0.004, peak_level=0.5),
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
