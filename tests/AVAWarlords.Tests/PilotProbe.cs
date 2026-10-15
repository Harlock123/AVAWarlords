using AVAWarlords.Engine;

namespace AVAWarlords.Tests;

/// <summary>How the computer gets on at your castle, round by round (set WARLORDS_PROBE to run it, for tuning; WARLORDS_PROBE=lazy for a player who never moves).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("WARLORDS_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "warlords-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var results = new List<string>();
            var last = g.Phase;
            int t = 0, won = 0, lost = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Round <= 8 && results.Count < 12; t++)
            {
                var c = mode == "lazy" ? default : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase != last && g.Phase is Phase.Won or Phase.Lost)
                {
                    bool w = g.Phase == Phase.Won;
                    if (w) won++; else lost++;
                    results.Add($"R{g.Round}{(w ? "W" : "L")}:{(t - start) / 60:0}s/{g.Castles.Count(k => k.Alive)}");
                }
                if (g.Phase == Phase.Start && last != Phase.Start) start = t;
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: round {g.Round} after {t / 60:0}s score {g.Score} won {won} lost {lost} | {string.Join(" ", results)}");
        }
    }
}
