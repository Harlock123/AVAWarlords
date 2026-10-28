# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A four-castle battle in the style of Atari's 1980 arcade game Warlords, in C# on .NET 10 and Avalonia 12. You
hold one castle and the computer holds the other three. One self-contained executable per platform (Windows,
macOS and Linux, x64 and ARM). Everything in it is original: the graphics are drawn in code, and the sounds and
tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAWarlords.slnx                     # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAWarlords.slnx                      # every test runs headless: no window opens
dotnet run --project src/AVAWarlords              # play it; --no-audio for silence
WARLORDS_SHOTS=$PWD/Images dotnet test tests/AVAWarlords.Tests --filter ScreenshotTests   # refresh the README's pictures
WARLORDS_PROBE=1 dotnet test tests/AVAWarlords.Tests --filter PilotProbe   # how the computer does at your castle ("lazy" for no input)
python3 tools/make_sounds.py                      # rebuild src/AVAWarlords/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAWarlords/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the phases of a round.
  - `GameEngine.Play.cs` steps a round: the dragon's fireballs, bricks knocked out of the walls, your shield,
    catching and flinging, and the three computer warlords (`Think`).
  - `Entities.cs` has `Controls`, the battlefield measures (`Field`; each castle on its own grid counted from
    its corner) and the castles and fireballs.
  - `WarlordPilot.cs` plays your castle for the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAWarlords/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame and runs the
  60 Hz timer.
- `src/AVAWarlords/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAWarlords.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `PilotProbe` does nothing unless
  `WARLORDS_PROBE` is set.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so a seed always plays the same game. Draw random numbers from the engine's seeded `_rng`, never from
  `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Negative `Dy` is up the screen. The shield only slides one way or the
  other round the castle: up or left slide it one way, down or right the other. Test new controls through
  `InputHandler`, not by setting `Controls` directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAWarlords`); tests load
  the table from a temporary folder and run the engine with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
