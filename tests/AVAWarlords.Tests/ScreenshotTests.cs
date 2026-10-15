using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAWarlords.Engine;
using AVAWarlords.Views;

namespace AVAWarlords.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set WARLORDS_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avawarlords-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("WARLORDS_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAWarlords-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("LRD", 84250, 9);
        g.HighScores.Insert("AVA", 52800, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_battle()
    {
        var (w, c, g) = Open(2);
        g.StartGame();
        g.SetLives(9);
        g.Idle(60);
        c.Advance(.5);
        Shot(w, c, "round-start");
        Assert.True(PlayUntil(g, c, g => g.Balls.Count(b => b.HeldBy == null) >= 2 && g.Castles.Count(k => k.BricksLeft < 30) >= 2 && g.Castles.All(k => k.Alive), 200));
        Shot(w, c, "battle");
        Assert.True(PlayUntil(g, c, g => g.Balls.Any(b => b.HeldBy == g.Castles[0]) && g.Phase == Phase.Play, 200));
        Shot(w, c, "catch");
        Assert.True(PlayUntil(g, c, g => g.Effects.Any(e => e.Kind == EffectKind.Fall && e.Age > .35), 300));
        Shot(w, c, "fallen");
    }

    [AvaloniaFact]
    public void Victory_and_defeat()
    {
        var (w, c, g) = Open(3);
        g.Go(4);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Won && g.PhaseTime > .8, 400));
        Shot(w, c, "victory");
        g.Go(2);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.RoundTime > 6));
        g.FellForTest(GameEngine.You);
        for (int t = 0; t < 40; t++) { g.Tick(default); c.Advance(1 / 60.0); }
        Shot(w, c, "defeat");
    }
}
