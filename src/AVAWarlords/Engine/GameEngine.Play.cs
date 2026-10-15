namespace AVAWarlords.Engine;

public sealed partial class GameEngine
{
    /// <summary>Shield speeds along the path, in cells a second: yours, and the computer warlords' to start with.</summary>
    public const double ShieldSpeed = 22, AiShieldSpeed = 7.5;
    public const double BallStart = 270, BallMax = 640, SpeedUp = 1.04, ThrowBoost = 1.25;
    public const double StartSeconds = 2.6, LostSeconds = 3.0, WonSeconds = 3.5, HoldMax = 3.0;
    /// <summary>When in a round the dragon breathes another fireball.</summary>
    public static readonly double[] Breaths = [1.0, 14, 30, 48, 68, 90, 115];
    public const int MaxBalls = 4;
    public const int BrickPoints = 10, KillPoints = 500, SurvivePoints = 100, WinPoints = 1000;
    public const int FirstExtraLife = 15000, ExtraLifeEvery = 40000;
    /// <summary>Your castle: the one in the bottom left.</summary>
    public const int You = 0;

    public int Round { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public List<Castle> Castles { get; } = [];
    public List<Ball> Balls { get; } = [];
    public WarlordPilot Pilot { get; } = new();
    public double RoundTime { get; private set; }
    /// <summary>For tests: the dragon breathes no fireballs and the computer warlords hold still.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _breath;
    private double _quiet;
    private bool _firePrev;

    public double Pace => Math.Min(1.6, 1 + .08 * (Round - 1));
    public double AiSpeed => Math.Min(17, AiShieldSpeed + 1.0 * (Round - 1));
    /// <summary>How far off a computer warlord may misjudge where a fireball will come (in cells).</summary>
    public double AiMisjudge => Math.Max(.5, 2.3 - .2 * (Round - 1));

    private static readonly (string Name, uint Colour, double Ox, double Oy, int Sx, int Sy)[] Lords =
    [
        ("LORD AZURE", 0xFF4AB0FF, Field.Left, Field.Bottom, 1, -1),
        ("LORD CRIMSON", 0xFFFF4A4A, Field.Left, Field.Top, 1, 1),
        ("LORD GOLD", 0xFFFFD040, Field.Right, Field.Top, -1, 1),
        ("LORD VERDANT", 0xFF4AE06A, Field.Right, Field.Bottom, -1, -1),
    ];

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Round = 1;
        _nextExtra = FirstExtraLife;
        LayOut();
        SetPhase(Phase.Start);
    }

    private void BeginRound(int round)
    {
        Round = round;
        LayOut();
        SetPhase(Phase.Start);
        Say($"ROUND {round}", "DEFEND YOUR CASTLE - LORD AZURE", StartSeconds);
        Play(Sfx.RoundStart);
    }

    private void LayOut()
    {
        if (Castles.Count == 0)
            for (int i = 0; i < 4; i++)
            {
                var (name, colour, ox, oy, sx, sy) = Lords[i];
                Castles.Add(new Castle { Index = i, Name = name, Colour = colour, Ox = ox, Oy = oy, Sx = sx, Sy = sy });
            }
        foreach (var c in Castles) { c.Rebuild(); c.Hold = 0; c.Target = -1; }
        Balls.Clear();
        Effects.Clear();
        RoundTime = 0;
        _breath = 0;
        _quiet = 0;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                _firePrev = c.Fire;
                MoveShield(Castles[You], c, dt);
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                RoundTime += dt;
                Breathe(dt);
                MoveShield(Castles[You], c, dt);
                foreach (var castle in Castles.Where(k => k.Index != You && k.Alive)) Think(castle, dt);
                MoveBalls(dt);
                if (Phase == Phase.Play) Outcome();
                break;
            case Phase.Lost:
                MoveBalls(dt);
                if (PhaseTime > LostSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    BeginRound(Round);
                }
                break;
            case Phase.Won:
                if (PhaseTime > WonSeconds) BeginRound(Round + 1);
                break;
        }
    }

    /// <summary>
    /// The dragon in the middle breathes a fireball at one of the castles still standing: at set times early in a
    /// round, every so often after, and straight away whenever there's none left in play.
    /// </summary>
    private void Breathe(double dt)
    {
        if (Calm) return;
        _quiet = Balls.Any(b => !b.Dead) ? 0 : _quiet + dt;
        bool due = _breath < Breaths.Length ? RoundTime >= Breaths[_breath] : RoundTime >= Breaths[^1] + 22 * (_breath - Breaths.Length + 1);
        if (!due && _quiet < 1.5) return;
        if (due) _breath++;
        _quiet = 0;
        if (Balls.Count(b => !b.Dead) >= Math.Min(MaxBalls, Castles.Count(k => k.Alive))) return;
        var targets = Castles.Where(k => k.Alive).ToList();
        var t = targets[_rng.Next(targets.Count)];
        var (kx, ky) = t.King;
        double a = Math.Atan2(ky - Field.CentreY, kx - Field.CentreX) + (_rng.NextDouble() - .5) * .7;
        Launch(Field.CentreX, Field.CentreY, a, BallStart * Pace);
        Effects.Add(new Effect { Kind = EffectKind.Breath, X = Field.CentreX, Y = Field.CentreY, Life = .8, Size = a });
        Play(Sfx.Breath);
    }

    private Ball Launch(double x, double y, double angle, double speed)
    {
        var b = new Ball { X = x, Y = y, Speed = speed, Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed };
        Balls.Add(b);
        return b;
    }

    // ───────────────────────────── the shields ─────────────────────────────

    /// <summary>Yours: any way round slides it (up or left one way, down or right the other), and the button catches.</summary>
    private void MoveShield(Castle k, Controls c, double dt)
    {
        bool fire = c.Fire, released = _firePrev && !fire;
        _firePrev = fire;
        if (!k.Alive) return;
        int dir = c.Dx < 0 || c.Dy < 0 ? 1 : c.Dx > 0 || c.Dy > 0 ? -1 : 0;
        Slide(k, k.S + dir * 100, ShieldSpeed, dt);
        if (Balls.FirstOrDefault(b => b.HeldBy == k) is { } held)
        {
            held.HeldTime += dt;
            if (released || !fire || held.HeldTime > HoldMax) Release(held);
        }
        k.CatchNext = fire;
    }

    private static void Slide(Castle k, double target, double speed, double dt)
    {
        target = Math.Clamp(target, 0, Castle.PathLength);
        k.S += Math.Clamp(target - k.S, -speed * dt, speed * dt);
    }

    /// <summary>A computer warlord: shield to where the next fireball will come, sometimes catching it to fling at a rival.</summary>
    private void Think(Castle k, double dt)
    {
        if (Calm) return;
        if (Balls.FirstOrDefault(b => b.HeldBy == k) is { } held)
        {
            held.HeldTime += dt;
            // Aim at a rival's warlord, then let fly.
            if (k.Target < 0 || !Castles[k.Target].Alive)
            {
                var rivals = Castles.Where(r => r.Alive && r != k).ToList();
                k.Target = rivals.Count > 0 ? rivals[_rng.Next(rivals.Count)].Index : -1;
            }
            double aim = k.Target >= 0 ? AimAt(k, Castles[k.Target]) : Castle.Lane;
            Slide(k, aim, AiSpeed, dt);
            if (Math.Abs(k.S - aim) < .2 || held.HeldTime > 1.6) Release(held);
            return;
        }
        var (want, eta) = Intercept(this, k);
        if (eta < 0) { Slide(k, Castle.Lane, AiSpeed * .5, dt); return; }
        Slide(k, want + k.AimError, AiSpeed, dt);
    }

    /// <summary>Where along its path a castle's shield should meet the next fireball coming its way, and how soon (-1: none coming).</summary>
    public static (double S, double Eta) Intercept(GameEngine g, Castle k)
    {
        (double S, double Eta) best = (Castle.Lane, -1);
        foreach (var b in g.Balls)
        {
            var (s, eta) = Arrival(b, k);
            if (eta >= 0 && (best.Eta < 0 || eta < best.Eta)) best = (s, eta);
        }
        return best;
    }

    /// <summary>Where along a castle's path one fireball will arrive, and how soon (-1: not within a couple of seconds).</summary>
    public static (double S, double Eta) Arrival(Ball b, Castle k)
    {
        if (b.Dead || b.HeldBy != null) return (Castle.Lane, -1);
        double x = b.X, y = b.Y, vx = b.Vx, vy = b.Vy, step = .01;
        for (double t = 0; t < 1.8; t += step)
        {
            x += vx * step; y += vy * step;
            if (x < Field.Left + Ball.Radius || x > Field.Right - Ball.Radius) vx = -vx;
            if (y < Field.Top + Ball.Radius || y > Field.Bottom - Ball.Radius) vy = -vy;
            var (u, v) = k.ToLocal(x, y);
            if (Math.Max(u, v) <= Castle.Lane + .9 && u >= -.5 && v >= -.5) return (Castle.PathAt(u, v), t);
        }
        return (Castle.Lane, -1);
    }

    /// <summary>The place along a castle's path from which a fireball flung outward goes at a rival's warlord.</summary>
    public static double AimAt(Castle k, Castle rival)
    {
        var (kx, ky) = k.King;
        var (rx, ry) = rival.King;
        double want = Math.Atan2(ry - ky, rx - kx), best = Castle.Lane, err = double.MaxValue;
        for (double s = 0; s <= Castle.PathLength; s += .1)
        {
            var (u, v) = Castle.PathPoint(s);
            var (px, py) = k.ToWorld(u, v);
            double a = Math.Atan2(py - ky, px - kx), d = Math.Abs(Math.Atan2(Math.Sin(a - want), Math.Cos(a - want)));
            if (d < err) { err = d; best = s; }
        }
        return best;
    }

    /// <summary>A held fireball flies off outward, along the line from the warlord through the shield.</summary>
    private void Release(Ball b)
    {
        var k = b.HeldBy!;
        var (kx, ky) = k.King;
        double dx = b.X - kx, dy = b.Y - ky, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        // Flung harder than it came.
        b.Speed = Math.Min(BallMax * Pace * 1.2, b.Speed * ThrowBoost);
        b.Vx = dx / d * b.Speed;
        b.Vy = dy / d * b.Speed;
        b.HeldBy = null;
        b.HeldTime = 0;
        k.Target = -1;
        Play(Sfx.Launch);
    }

    // ───────────────────────────── the fireballs ─────────────────────────────

    private void MoveBalls(double dt)
    {
        foreach (var b in Balls)
        {
            if (b.Dead) continue;
            if (b.HeldBy is { } k)
            {
                // Riding on the shield, just outside it.
                var (u, v) = Castle.PathPoint(k.S);
                if (k.S <= Castle.Lane) u += .65; else v += .65;
                (b.X, b.Y) = k.ToWorld(u, v);
                if (!k.Alive) Release(b);
                continue;
            }
            // Fireballs grow fiercer the longer a round lasts.
            b.Speed = Math.Min(BallMax * Pace, b.Speed * (1 + .012 * dt));
            double sp = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
            if (sp > 0) { b.Vx *= b.Speed / sp; b.Vy *= b.Speed / sp; }
            double dist = b.Speed * dt;
            int steps = (int)Math.Ceiling(dist / 3);
            for (int i = 0; i < steps && !b.Dead && b.HeldBy == null; i++)
            {
                double px = b.X, py = b.Y;
                b.X += b.Vx * dt / steps;
                b.Y += b.Vy * dt / steps;
                // The edges of the field.
                bool wall = false;
                if (b.X < Field.Left + Ball.Radius) { b.X = Field.Left + Ball.Radius; b.Vx = Math.Abs(b.Vx); wall = true; }
                if (b.X > Field.Right - Ball.Radius) { b.X = Field.Right - Ball.Radius; b.Vx = -Math.Abs(b.Vx); wall = true; }
                if (b.Y < Field.Top + Ball.Radius) { b.Y = Field.Top + Ball.Radius; b.Vy = Math.Abs(b.Vy); wall = true; }
                if (b.Y > Field.Bottom - Ball.Radius) { b.Y = Field.Bottom - Ball.Radius; b.Vy = -Math.Abs(b.Vy); wall = true; }
                if (wall)
                {
                    // A slight kick off the wall, so no fireball is caught going back and forth for ever.
                    double a = Math.Atan2(b.Vy, b.Vx) + (_rng.NextDouble() - .5) * .16;
                    // Never quite straight across or straight up and down.
                    double off = Math.Abs(Math.Sin(2 * a));
                    if (off < .25) a += .2 * Math.Sign(Math.Sin(2 * a) == 0 ? 1 : Math.Sin(2 * a));
                    (b.Vx, b.Vy) = (Math.Cos(a) * b.Speed, Math.Sin(a) * b.Speed);
                    Play(Sfx.Wall);
                }
                foreach (var c in Castles) Meet(b, c, px, py);
            }
        }
        Balls.RemoveAll(b => b.Dead);
    }

    /// <summary>A fireball meeting a castle: its shield, a brick, or the warlord.</summary>
    private void Meet(Ball b, Castle c, double px, double py)
    {
        if (b.Dead || b.HeldBy != null) return;
        var (u, v) = c.ToLocal(b.X, b.Y);
        if (u < -.5 || v < -.5 || u > Field.Cells + .5 || v > Field.Cells + .5) return;
        // The shield.
        if (c.Alive && ShieldHit(c, u, v) is { } leg)
        {
            var (pu, pv) = c.ToLocal(px, py);
            if (c.CatchNext && Balls.All(o => o.HeldBy != c))
            {
                b.HeldBy = c;
                b.HeldTime = 0;
                b.Owner = c.Index;
                c.Hold = 0;
                Play(Sfx.Catch);
                return;
            }
            Bounce(b, c, leg, u, v, pu, pv);
            return;
        }
        int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
        if (i < 0 || j < 0 || i >= Field.Cells || j >= Field.Cells) return;
        // A brick.
        if (c.Bricks[i, j])
        {
            c.Bricks[i, j] = false;
            var (pu, pv) = c.ToLocal(px, py);
            int pi = (int)Math.Floor(pu), pj = (int)Math.Floor(pv);
            bool flipU = pi != i, flipV = pj != j;
            if (!flipU && !flipV) flipU = flipV = true;
            if (flipU) b.Vx = -b.Vx;
            if (flipV) b.Vy = -b.Vy;
            (b.X, b.Y) = (px, py);
            var (bx, by) = c.ToWorld(i + .5, j + .5);
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = bx, Y = by, Colour = c.Colour, Size = .45, Life = .4 });
            if (b.Owner == You) AddScore(BrickPoints);
            Play(Sfx.Brick);
            return;
        }
        // The warlord.
        if (c.Alive && u < Castle.KingSize && v < Castle.KingSize)
        {
            Fall(c, b.Owner);
            b.Vx = -b.Vx;
            b.Vy = -b.Vy;
            (b.X, b.Y) = (px, py);
        }
    }

    /// <summary>Whether a point (in a castle's cells) touches its shield: 0 on the leg up the side, 1 on the leg along the top.</summary>
    private static int? ShieldHit(Castle c, double u, double v)
    {
        double r = Ball.Radius / Field.Cell;
        double lo = c.S - Castle.Half, hi = c.S + Castle.Half;
        // The leg up the side (u = Lane, v from 0 to Lane).
        if (lo <= Castle.Lane && Math.Abs(u - Castle.Lane) < Castle.Thick + r && v >= Math.Max(0, lo) - r && v <= Math.Min(Castle.Lane, hi) + r) return 0;
        // The leg along the top (v = Lane, u from Lane down to 0).
        if (hi >= Castle.Lane && Math.Abs(v - Castle.Lane) < Castle.Thick + r)
        {
            double uFrom = Castle.PathLength - Math.Min(Castle.PathLength, hi), uTo = Castle.PathLength - Math.Max(Castle.Lane, lo);
            if (u >= uFrom - r && u <= uTo + r) return 1;
        }
        return null;
    }

    /// <summary>Off the shield: the side it came from decides which way it goes, and where it struck along the shield angles it.</summary>
    private void Bounce(Ball b, Castle c, int leg, double u, double v, double pu, double pv)
    {
        b.Speed = Math.Min(BallMax * Pace, b.Speed * SpeedUp);
        // In local terms: the normal component out (or in, if it came from inside), the other kept and nudged.
        double du = b.Vx * c.Sx, dv = b.Vy * c.Sy;
        double along = c.S - Castle.PathAt(u, v);
        if (leg == 0)
        {
            du = (pu >= Castle.Lane ? 1 : -1) * Math.Max(Math.Abs(du), b.Speed * .45);
            dv += -along * b.Speed * .35;
        }
        else
        {
            dv = (pv >= Castle.Lane ? 1 : -1) * Math.Max(Math.Abs(dv), b.Speed * .45);
            du += along * b.Speed * .35;
        }
        double len = Math.Sqrt(du * du + dv * dv);
        b.Vx = du / len * b.Speed * c.Sx;
        b.Vy = dv / len * b.Speed * c.Sy;
        // Out of the shield, so it doesn't strike again at once.
        var (wx, wy) = c.ToWorld(leg == 0 ? (pu >= Castle.Lane ? Castle.Lane + Castle.Thick + .3 : Castle.Lane - Castle.Thick - .3) : u,
                                 leg == 1 ? (pv >= Castle.Lane ? Castle.Lane + Castle.Thick + .3 : Castle.Lane - Castle.Thick - .3) : v);
        (b.X, b.Y) = (wx, wy);
        b.Owner = c.Index;
        if (c.Index != You)
        {
            // A computer warlord sizes up the next one: how well it'll judge it, and whether to catch it.
            c.AimError = (_rng.NextDouble() * 2 - 1) * AiMisjudge;
            c.CatchNext = _rng.NextDouble() < .3;
        }
        Play(Sfx.Shield);
    }

    private void Fall(Castle c, int by)
    {
        c.Alive = false;
        var (kx, ky) = c.King;
        Effects.Add(new Effect { Kind = EffectKind.Fall, X = kx, Y = ky, Life = 1.6, Colour = c.Colour, Size = 1.4 });
        foreach (var held in Balls.Where(h => h.HeldBy == c).ToList()) Release(held);
        // No more fireballs in play than castles standing: the newest goes out in a puff.
        int standing = Castles.Count(k => k.Alive);
        foreach (var extra in Balls.Where(x => !x.Dead && x.HeldBy == null).Reverse().Skip(standing).ToList())
        {
            extra.Dead = true;
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = extra.X, Y = extra.Y, Colour = 0xFF8A8078, Size = .8, Life = .6 });
        }
        Play(Sfx.Fall);
        if (c.Index == You) return;
        int points = SurvivePoints + (by == You ? KillPoints : 0);
        AddScore(points, kx, ky);
        Say($"{c.Name} HAS FALLEN", by == You ? $"BY YOUR HAND - {points}" : null, 1.8);
    }

    private void Outcome()
    {
        if (!Castles[You].Alive)
        {
            Lives--;
            SetPhase(Phase.Lost);
            Music(false);
            Say("YOUR CASTLE HAS FALLEN", Lives > 0 ? "TRY THE ROUND AGAIN" : null, LostSeconds);
            Play(Sfx.Lost);
            return;
        }
        if (Castles.Count(k => k.Alive) == 1)
        {
            int bonus = WinPoints * Round;
            AddScore(bonus);
            SetPhase(Phase.Won);
            Music(false);
            Balls.Clear();
            Say("VICTORY!", $"ROUND BONUS {bonus}", WonSeconds);
            Play(Sfx.Won);
        }
    }

    private void AddScore(int points, double? x = null, double? y = null)
    {
        Score += points;
        if (x is { } px && y is { } py)
            Effects.Add(new Effect { Kind = EffectKind.Score, X = px, Y = py, Text = points.ToString(), Colour = 0xFFFFE14A, Life = 1.2 });
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a round.</summary>
    internal void Go(int round = 1)
    {
        StartGame();
        BeginRound(round);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal Ball PutBall(double x, double y, double vx, double vy)
    {
        double sp = Math.Sqrt(vx * vx + vy * vy);
        var b = new Ball { X = x, Y = y, Vx = vx, Vy = vy, Speed = sp };
        Balls.Add(b);
        return b;
    }

    /// <summary>A fireball at a point in a castle's cells, heading the given way (in its cells too).</summary>
    internal Ball PutBallAt(int castle, double u, double v, double du, double dv)
    {
        var c = Castles[castle];
        var (x, y) = c.ToWorld(u, v);
        return PutBall(x, y, du * c.Sx, dv * c.Sy);
    }

    internal void FellForTest(int castle, int by = -1) => Fall(Castles[castle], by);
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
    internal void SetRoundTime(double t) => RoundTime = t;
}
