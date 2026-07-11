#!/usr/bin/env python3
"""Generates the game's ability/impact SOUND EFFECTS with the ElevenLabs sound-generation API.

Usage:
    export ELEVENLABS_API_KEY=sk_...        # never hardcode/commit the key
    python3 Tools/generate_elevenlabs_sfx.py             # generate everything missing
    python3 Tools/generate_elevenlabs_sfx.py --force     # regenerate everything
    python3 Tools/generate_elevenlabs_sfx.py --only zap_charge zap_fire   # just these
    python3 Tools/generate_elevenlabs_sfx.py --list      # show the table

Each entry is (prompt, duration_seconds, prompt_influence). Durations are matched to the
gameplay moment (e.g. zap_charge = exactly the 3.0 s ZapSession.ChargeDuration). Output is
MP3 44.1 kHz into Assets/Resources/Audio/Sfx/ - SfxPlayer loads clips by file name, so a
generated file is immediately playable via SfxPlayer.Play("<name>"). Rerunning a single
name is the tuning loop: tweak the prompt here, regenerate with --only, listen in Unity.

The API costs credits per second of generated audio; the full table is ~20 s total.
Docs: POST https://api.elevenlabs.io/v1/sound-generation
      body {"text", "duration_seconds" (0.5-22), "prompt_influence" (0-1)}
"""
import argparse, json, os, sys, urllib.request

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Audio", "Sfx")
API_URL = "https://api.elevenlabs.io/v1/sound-generation"

STYLE = ("Punchy satisfying sound effect for a charming cartoony stone-brick puzzle game. "
         "Full-bodied, crisp attack, warm low-mid body, playful and earthy magic. "
         "Not sci-fi, no metal, no chimes, no coins, no music, no voice. ")

# name -> (prompt, duration_seconds, prompt_influence)
# KEEPERS (loved, do NOT regenerate): fission_feed, maw_crunch, flip_swap, zap_charge (engineered wav).
SOUNDS = {
    "shatter_zap": (STYLE + "A stone brick blasted apart by a magic bolt: one punchy deep "
        "crack-POP with chunky stone debris scattering, energetic and satisfying.", 0.9, 0.55),
    "shatter_bomb": (STYLE + "A big cartoon powder-keg explosion: one fat deep BOOM with wood "
        "splinters and stones raining down briefly, powerful with comedic weight.", 1.4, 0.5),
    "shatter_sacrifice": (STYLE + "A stone brick zapped into dust by an energy line: quick "
        "sharp fizz-crack then a soft whoosh of dust falling.", 1.0, 0.5),
    "shatter_generic": (STYLE + "A stone brick cracking and breaking apart: one solid stone "
        "CRACK with rubble tumbling, dry and punchy.", 0.8, 0.55),
    "maw_crunch": (STYLE + "A monster devouring a stone block: one wet powerful crunch bite "
        "with a gulping swallow, cartoonish and meaty.", 0.9, 0.55),  # KEEPER
    "zap_charge": (STYLE + "(engineered wav - do not regenerate without redoing the "
        "envelope shaping, see session notes)", 3.0, 0.6),  # KEEPER
    "zap_fire": (STYLE + "A powerful magic bolt striking a brick: one huge deep THWACK-boom "
        "with a crackling energy burst, mighty and punchy.", 0.8, 0.55),
    "zap_dud": (STYLE + "A big spell fizzling into nothing: comical deflating fizzle-pfft "
        "with a little puff, funny and clear.", 0.7, 0.5),
    "zap_dematerialize": (STYLE + "A toy block whooshing up into the air and popping away: "
        "quick rising whoosh with an airy pop, light and playful.", 0.7, 0.5),
    "fission_split": (STYLE + "A stone block bursting into four pieces: one meaty CRACK-pop "
        "with pieces knocking together like wooden blocks, playful and punchy.", 0.8, 0.55),
    "fission_feed": (STYLE + "A small shard materializing: quick soft pop with a tiny "
        "sparkle.", 0.5, 0.5),  # KEEPER
    "overdraw_open": (STYLE + "Three big playing cards fanned out with flair: crisp fast "
        "triple card-flick whoosh, snappy and playful.", 0.9, 0.5),
    "overdraw_pick": (STYLE + "Picking a card: one crisp satisfying card-snap flick with a "
        "soft thump landing.", 0.5, 0.5),
    "laser_line_on": (STYLE + "A magical energy tripwire snapping on: quick energetic "
        "swoosh-snap settling into a brief warm hum.", 0.9, 0.5),
    "hardline_catch": (STYLE + "A falling stone brick caught midair by magic and set down as "
        "a platform: quick catch whoosh then one BIG satisfying stone THUNK.", 1.0, 0.55),
    "extract_open": (STYLE + "Time stopping dramatically: deep air-rush whoosh sweeping in "
        "and holding still, breathy and cinematic.", 0.9, 0.5),
    "extract_delete": (STYLE + "A stone brick yanked out of existence: one satisfying deep "
        "PLOP with a quick air suck.", 0.7, 0.55),
    "suspension_lock": (STYLE + "A heavy stone block slammed permanently into place: one big "
        "weighty stone SLAM with a solid resonant settle.", 0.9, 0.55),
    "rescue_beam": (STYLE + "A block scooped up by a rising gust of wind: strong upward "
        "whoosh swelling as it lifts away, airy and uplifting.", 1.0, 0.5),
    "ability_offer": (STYLE + "Reward cards dealt onto a table with flair: quick lively "
        "card-dealing flutter whoosh, crisp and inviting.", 0.7, 0.5),
    "ability_pick": (STYLE + "Confirming a choice: one solid satisfying wooden KNOCK with a "
        "warm short poof, punchy and rewarding.", 0.7, 0.55),
    "ward_absorb": (STYLE + "A magic shield blocking a hit: one deep resonant WOMP thud with "
        "a short air ripple, protective.", 0.8, 0.5),
    "flip_swap": (STYLE + "Two playing cards swapping instantly: crisp double card-flip "
        "whoosh, snappy and quick.", 0.5, 0.5),  # KEEPER
    "slowmo_engage": (STYLE + "Time bending into slow motion: one big smooth deep WHOOM "
        "sweep dropping down, dramatic.", 1.0, 0.5),
    "life_lost": (STYLE + "Losing a life in a puzzle game: one deep soft muffled thud with "
        "a short sad downward whoosh, weighty but not harsh, brief.", 0.9, 0.55),
    "game_over": (STYLE + "Game over: three slow deep muffled drum thuds descending in pitch, "
        "dark heavy and somber, very low frequency, absolutely no melodic or high tones.", 1.6, 0.6),
    "status_engage": (STYLE + "A protective wind aura whooshing around: full circular gust "
        "swirl, breathy and energetic.", 0.9, 0.5),

    # ---- Tier-0 landing layers (JUICE.md §5): every landing mixes transient + body (+ tail
    # on gentle placements), so each layer must be a SINGLE clean hit with no extra events.
    # Round-robins: same prompt, separate generations = natural variation.
    "land_body_01": (STYLE + "A toy stone brick landing flat on a stone tower: one single "
        "deep dry compact THUD with warm low body, very short, no debris, no bounce.", 0.6, 0.55),
    "land_body_02": (STYLE + "A toy stone brick landing flat on a stone tower: one single "
        "deep dry compact THUD with warm low body, very short, no debris, no bounce.", 0.6, 0.55),
    "land_body_03": (STYLE + "A toy stone brick landing flat on a stone tower: one single "
        "deep dry compact THUD with warm low body, very short, no debris, no bounce.", 0.6, 0.55),
    "land_transient_01": (STYLE + "One ultra-short crisp tick of a small stone block seating "
        "precisely into place: clean close-mic'd tap, instant, no reverb, no tail.", 0.4, 0.55),
    "land_transient_02": (STYLE + "One ultra-short crisp tick of a small stone block seating "
        "precisely into place: clean close-mic'd tap, instant, no reverb, no tail.", 0.4, 0.55),
    "land_transient_03": (STYLE + "One ultra-short crisp tick of a small stone block seating "
        "precisely into place: clean close-mic'd tap, instant, no reverb, no tail.", 0.4, 0.55),
    "land_tail_01": (STYLE + "Fine sand and grit settling briefly after a block lands: one "
        "soft short airy shf of dust, very quiet, gentle, no impact sound.", 0.7, 0.5),
    "land_tail_02": (STYLE + "Fine sand and grit settling briefly after a block lands: one "
        "soft short airy shf of dust, very quiet, gentle, no impact sound.", 0.7, 0.5),
    # Airtight mode - the air-pocket hazard (seal -> fill -> vent or pop).
    "pocket_seal": (STYLE + "Air suddenly trapped in a sealed stone chamber: one deep muffled "
        "pressure WHUMP with a claustrophobic airless quality, low sub-bass, short, ominous.", 0.9, 0.55),
    "pocket_vent": (STYLE + "Pressurized air escaping a stone chamber through a fresh opening: "
        "one sharp satisfying steam-hiss decompression falling to silence, relief, short.", 1.0, 0.55),
    "pocket_pop": (STYLE + "A sealed pocket of pressure detonating inside stonework: one fat "
        "muffled underground BOOM with a stone chamber burst and brief debris, deep and heavy.", 1.3, 0.5),
    # Blackout status - the district loses power for ~20s (LEVELS.md scheduled pressure).
    "blackout_in": (STYLE + "A city district losing electrical power: one deep descending "
        "power-down WHOMP, a big machine hum dying away to silence, heavy and ominous.", 1.6, 0.55),
    "blackout_out": (STYLE + "Electrical power returning to a city district: breakers clunking "
        "back on and a warm machine hum swelling up from silence, relieving, short.", 1.4, 0.55),
}


def _normalize_to_wav(mp3_bytes, out_wav, target_rms_db=-14.0):
    """Loudness is engineered, not gambled: decode, RMS-normalize to a punchy consistent
    level, peak-limit, write 16-bit WAV. Kills the 'this one is inaudible' failure mode."""
    import subprocess, tempfile, wave
    with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as f:
        f.write(mp3_bytes); tmp_mp3 = f.name
    tmp_wav = tmp_mp3.replace(".mp3", ".wav")
    subprocess.run(["afconvert", "-f", "WAVE", "-d", "LEI16@44100", tmp_mp3, tmp_wav], check=True)
    w = wave.open(tmp_wav); n = w.getnframes(); sr = w.getframerate(); ch = w.getnchannels()
    raw = w.readframes(n); w.close()
    import array, math
    data = array.array("h", raw)
    if ch == 2:  # downmix
        data = array.array("h", [(data[i] + data[i+1]) // 2 for i in range(0, len(data), 2)])
    rms = math.sqrt(sum(s*s for s in data) / max(1, len(data))) / 32768.0
    gain = (10 ** (target_rms_db / 20.0)) / max(rms, 1e-6)
    peak = max(1, max(abs(s) for s in data)) / 32768.0
    gain = min(gain, 0.95 / peak)  # peak limit
    out = array.array("h", [max(-32768, min(32767, int(s * gain))) for s in data])
    with wave.open(out_wav, "w") as ww:
        ww.setnchannels(1); ww.setsampwidth(2); ww.setframerate(sr)
        ww.writeframes(out.tobytes())
    os.remove(tmp_mp3); os.remove(tmp_wav)


def generate(name, prompt, duration, influence, key):
    body = json.dumps({"text": prompt, "duration_seconds": duration,
                       "prompt_influence": influence}).encode()
    req = urllib.request.Request(API_URL, data=body, headers={
        "xi-api-key": key, "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            audio = resp.read()
    except urllib.error.HTTPError as e:
        print(f"FAILED {name}: HTTP {e.code} {e.read().decode()[:200]}")
        return
    out = os.path.abspath(os.path.join(OUT_DIR, f"{name}.wav"))
    _normalize_to_wav(audio, out)
    # retire the older mp3 twin so Resources.Load never sees two clips with one name
    for stale in (f"{name}.mp3", f"{name}.mp3.meta"):
        p = os.path.join(OUT_DIR, stale)
        if os.path.exists(p): os.remove(p)
    print(f"{out}  ({duration:.1f}s, normalized)")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true", help="regenerate existing files")
    ap.add_argument("--only", nargs="*", help="generate only these names")
    ap.add_argument("--list", action="store_true", help="print the sound table")
    args = ap.parse_args()

    if args.list:
        for n, (p, d, i) in SOUNDS.items():
            print(f"{n:22s} {d:4.1f}s  {p[:80]}...")
        sys.exit(0)

    key = os.environ.get("ELEVENLABS_API_KEY")
    if not key:
        sys.exit("Set ELEVENLABS_API_KEY (never hardcode it).")

    names = args.only if args.only else list(SOUNDS.keys())
    for name in names:
        if name not in SOUNDS:
            print(f"unknown sound: {name}")
            continue
        out = os.path.join(OUT_DIR, f"{name}.mp3")
        if os.path.exists(out) and not args.force and not args.only:
            print(f"skip (exists): {name}")
            continue
        prompt, duration, influence = SOUNDS[name]
        generate(name, prompt, duration, influence, key)
