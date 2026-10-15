#!/usr/bin/env python3
"""Synthesises AVAWarlords's sound effects and tunes into ../src/AVAWarlords/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAWarlords", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The fireball off a shield, a brick, the field's edge.
    write("Shield.wav", mix(tone(0.12, 520, 780, "square", release=0.8), tone(0.06, 2000, 1200, "noise", release=1.0, gain=0.25)), 0.3)
    write("Brick.wav", mix(tone(0.1, 260, 120, "square", release=0.9), tone(0.12, 1600, 400, "noise", release=0.9, gain=0.7)), 0.34)
    write("Wall.wav", tone(0.06, 180, 140, "tri", release=0.9), 0.22)
    # Catching and flinging.
    write("Catch.wav", cat(tone(0.05, 300, 200, "square", release=0.6), tone(0.25, 200, 210, "tri", release=0.4, vibrato=0.1, vib_rate=30)), 0.3)
    write("Launch.wav", tone(0.25, 300, 1400, "saw", release=0.6), 0.28)
    # A castle falling: a great crash and crumbling.
    write("Fall.wav", cat(mix(tone(1.0, 2600, 80, "noise", release=0.5), tone(1.0, 160, 40, "saw", release=0.5, gain=0.8)),
                          *[tone(0.12, f, f * 0.7, "noise", release=0.8, gain=0.5) for f in [900, 700, 500, 400]]), 0.52)
    # The dragon's breath: a roaring whoosh.
    write("Breath.wav", mix(tone(0.8, 400, 1800, "noise", attack=0.08, release=0.5), tone(0.8, 90, 70, "saw", attack=0.05, release=0.5, vibrato=0.08, vib_rate=12, gain=0.6)), 0.4)
    write("RoundStart.wav", tune(150, [("D5", 0.5), ("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("A5", 1.5)], [("D3", 1), ("A2", 1), ("D3", 1.5)], kind="square"), 0.4)
    # Victory: a herald's fanfare. Defeat: a slow fall.
    write("Won.wav", tune(140, [("D5", 0.33), ("D5", 0.33), ("D5", 0.33), ("A5", 1), ("G5", 0.5), ("A5", 0.5), ("B5", 0.5), ("C#6", 0.5), ("D6", 2)],
                          [("D3", 1), ("A2", 1), ("D3", 1), ("G2", 1), ("A2", 1), ("D3", 2)], kind="square"), 0.46)
    write("Lost.wav", tune(80, [("A4", 1), ("F4", 1), ("D4", 1), ("C#4", 1), ("D4", 2)], [("D3", 2), ("Bb2", 2), ("A2", 2)], kind="tri"), 0.45)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["D6", "F#6", "A6", "D7", "A6", "D7"]]), 0.4)

    # The round's tune: a stately dance in D dorian over a drone (new). Loops.
    lead = [("D5", 1), ("E5", 0.5), ("F5", 0.5), ("G5", 1), ("A5", 1), ("B5", 0.5), ("A5", 0.5), ("G5", 1), ("F5", 1), ("E5", 1),
            ("D5", 1), ("C5", 0.5), ("D5", 0.5), ("E5", 1), ("A4", 2), ("-", 1),
            ("A5", 1), ("G5", 0.5), ("F5", 0.5), ("E5", 1), ("F5", 1), ("G5", 0.5), ("F5", 0.5), ("E5", 1), ("D5", 1), ("C5", 1),
            ("D5", 1), ("E5", 0.5), ("C5", 0.5), ("D5", 3), ("-", 1)]
    bass = [("D3", 2), ("A2", 2), ("D3", 2), ("C3", 2), ("D3", 2), ("A2", 2), ("D3", 2), ("A2", 2),
            ("D3", 2), ("C3", 2), ("D3", 2), ("A2", 2), ("D3", 2), ("C3", 2), ("D3", 4)]
    music = tune(116, lead, bass, kind="tri")
    drone = [0.12 * math.sin(2 * math.pi * 73.4 * i / RATE) for i in range(len(music))]
    write("Music.wav", mix(music, drone), 0.38)

    # The title: a herald's call and a march (new).
    lead = [("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("A5", 1.5), ("G5", 0.5), ("F5", 0.5), ("E5", 1), ("D5", 1), ("A4", 1),
            ("Bb4", 0.5), ("D5", 0.5), ("G5", 0.5), ("Bb5", 1.5), ("A5", 0.5), ("G5", 0.5), ("F5", 1), ("E5", 2),
            ("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("A5", 1.5), ("C6", 0.5), ("Bb5", 0.5), ("A5", 1), ("G5", 1), ("F5", 1),
            ("E5", 0.5), ("F5", 0.5), ("G5", 0.5), ("E5", 0.5), ("D5", 3), ("-", 1)]
    bass = [("D3", 2), ("D3", 2), ("A2", 2), ("D3", 2), ("G2", 2), ("G2", 2), ("A2", 2), ("A2", 2),
            ("D3", 2), ("F3", 2), ("C3", 2), ("Bb2", 2), ("A2", 2), ("D3", 4)]
    write("TitleTune.wav", tune(126, lead, bass, kind="square"), 0.4)
    write("StartTune.wav", tune(160, [("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 0.5), ("C6", 0.5), ("A5", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="square"), 0.45)
    write("GameOver.wav", tune(70, [("A5", 1), ("F5", 1), ("E5", 1), ("D5", 1), ("C#5", 1), ("D5", 3)], [("D3", 2), ("Bb2", 2), ("A2", 2), ("D3", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
