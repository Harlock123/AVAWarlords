namespace AVAWarlords.Engine;

/// <summary>
/// The computer at your castle, for the demonstration (and the tests). It slides the shield to where the next
/// fireball will come, catches it, swings round until the line from its warlord through the shield points at a
/// rival's warlord, and lets fly.
/// </summary>
public sealed class WarlordPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        if (g.Phase is not (Phase.Play or Phase.Start)) return default;
        var k = g.Castles[GameEngine.You];
        if (!k.Alive) return default;
        double want;
        bool hold;
        if (g.Balls.Any(b => b.HeldBy == k))
        {
            // Aim at the rival with the most of its walls gone.
            var rival = g.Castles.Where(r => r.Alive && r != k).OrderBy(r => r.BricksLeft).FirstOrDefault();
            want = rival != null ? GameEngine.AimAt(k, rival) : Castle.Lane;
            hold = Math.Abs(want - k.S) > .15;
            Doing = "aim";
        }
        else
        {
            var (s, eta) = GameEngine.Intercept(g, k);
            want = eta < 0 ? Castle.Lane : s;
            // Catch it only if nothing else will arrive while it's held and aimed.
            int coming = g.Balls.Count(b => GameEngine.Arrival(b, k) is { Eta: >= 0 and < 1.3 });
            hold = eta >= 0 && eta < .4 && coming <= 1;
            Doing = eta < 0 ? "wait" : "block";
        }
        int dx = want > k.S + .06 ? -1 : want < k.S - .06 ? 1 : 0;
        return new Controls(dx, 0, hold);
    }
}
