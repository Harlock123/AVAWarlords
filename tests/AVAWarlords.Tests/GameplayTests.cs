using AVAWarlords.Engine;

namespace AVAWarlords.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avawarlords-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A round in play with no fireballs unless a test throws one, and the computer warlords still.</summary>
    private static GameEngine Game(int round = 1)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(round);
        return g;
    }

    /// <summary>A real round, the dragon breathing and the warlords defending, with plenty of lives.</summary>
    private static GameEngine Real(int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls Hold = new(Fire: true);

    // ───────────────────────────── the castles ─────────────────────────────

    [Fact]
    public void Four_castles_stand_in_the_corners_each_with_three_walls_round_its_warlord()
    {
        var g = Game();
        Assert.Equal(4, g.Castles.Count);
        var corners = g.Castles.Select(c => (c.Ox, c.Oy)).ToHashSet();
        Assert.Equal(4, corners.Count);
        foreach (var c in g.Castles)
        {
            Assert.True(c.Alive);
            Assert.Equal(33, c.BricksLeft);
            Assert.Equal(Castle.Lane, c.S);
            var (kx, ky) = c.King;
            Assert.True(Math.Abs(kx - c.Ox) < 60 && Math.Abs(ky - c.Oy) < 60);
        }
        Assert.Equal(33, g.Castles[0].BricksLeft);
    }

    // ───────────────────────────── your shield ─────────────────────────────

    [Fact]
    public void Your_shield_slides_round_the_castle_and_stops_at_the_ends()
    {
        var g = Game();
        var you = g.Castles[GameEngine.You];
        g.Run(6, new Controls(Dy: -1));
        Assert.True(you.S > Castle.Lane);
        g.Run(120, new Controls(Dx: -1));
        Assert.Equal(Castle.PathLength, you.S);
        g.Run(120, new Controls(Dx: 1));
        Assert.Equal(0, you.S);
    }

    [Fact]
    public void A_fireball_bounces_off_a_shield_outward_faster_and_becomes_that_castle_s()
    {
        var g = Game();
        var b = g.PutBallAt(GameEngine.You, 10, 7, -300, 0);
        g.Idle(30);
        var (u, _) = g.Castles[0].ToLocal(b.X, b.Y);
        Assert.True(u > 8, $"u {u}");
        Assert.True(b.Vx > 0);
        Assert.True(b.Speed > 300);
        Assert.Equal(GameEngine.You, b.Owner);
    }

    [Fact]
    public void Holding_the_button_catches_a_fireball_and_letting_go_flings_it_out_from_the_warlord_harder()
    {
        var g = Game();
        var you = g.Castles[0];
        var b = g.PutBallAt(GameEngine.You, 10, 7, -300, 0);
        g.Run(30, Hold);
        Assert.Same(you, b.HeldBy);
        g.Run(10, new Controls(Dy: -1, Fire: true));
        Assert.Same(you, b.HeldBy);
        double speed = b.Speed;
        g.Idle(1);
        Assert.Null(b.HeldBy);
        var (kx, ky) = you.King;
        double out1 = Math.Atan2(b.Y - ky, b.X - kx), dir = Math.Atan2(b.Vy, b.Vx);
        Assert.True(Math.Abs(Math.Atan2(Math.Sin(out1 - dir), Math.Cos(out1 - dir))) < .05);
        Assert.True(b.Speed > speed);
    }

    // ───────────────────────────── bricks and warlords ─────────────────────────────

    [Fact]
    public void A_fireball_breaks_a_brick_and_bounces_back_and_your_own_are_worth_10()
    {
        var g = Game();
        var c = g.Castles[2];
        var b = g.PutBallAt(2, 7.2, 2.5, -300, 0);
        b.Owner = GameEngine.You;
        g.Idle(6);
        Assert.False(c.Bricks[6, 2]);
        Assert.Equal(32, c.BricksLeft);
        Assert.True(b.Vx * c.Sx > 0);
        Assert.Equal(GameEngine.BrickPoints, g.Score);
    }

    [Fact]
    public void A_fireball_through_to_a_warlord_brings_the_castle_down_and_more_points_if_it_was_yours()
    {
        var g = Game();
        var c = g.Castles[1];
        for (int i = 3; i < 8; i++) c.Bricks[i, 1] = false;
        var b = g.PutBallAt(1, 7.2, 1.5, -400, 0);
        b.Owner = GameEngine.You;
        g.Idle(30);
        Assert.False(c.Alive);
        Assert.Equal(GameEngine.SurvivePoints + GameEngine.KillPoints, g.Score);
        Assert.True(g.Castles[0].Alive);
    }

    // ───────────────────────────── rounds ─────────────────────────────

    [Fact]
    public void Losing_your_castle_costs_a_life_and_the_round_is_fought_again_and_the_last_life_ends_the_game()
    {
        var g = Game(2);
        g.SetLives(2);
        g.FellForTest(GameEngine.You);
        g.Idle(1);
        Assert.Equal(Phase.Lost, g.Phase);
        Assert.Equal(1, g.Lives);
        g.Idle((int)(60 * GameEngine.LostSeconds) + 5);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal(2, g.Round);
        Assert.All(g.Castles, c => Assert.True(c.Alive && c.BricksLeft == 33));
        g.Idle((int)(60 * GameEngine.StartSeconds) + 5);
        g.FellForTest(GameEngine.You);
        g.Idle((int)(60 * GameEngine.LostSeconds) + 10);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Being_the_last_castle_standing_wins_the_round_and_its_bonus()
    {
        var g = Game(2);
        for (int i = 1; i < 4; i++) g.FellForTest(i);
        g.Idle(1);
        Assert.Equal(Phase.Won, g.Phase);
        Assert.Equal(3 * GameEngine.SurvivePoints + 2 * GameEngine.WinPoints, g.Score);
        g.Idle((int)(60 * GameEngine.WonSeconds) + 5);
        Assert.Equal(3, g.Round);
    }

    [Fact]
    public void The_dragon_breathes_a_fireball_at_the_start_and_again_whenever_none_are_left()
    {
        var g = Real();
        g.Idle(70);
        Assert.Single(g.Balls);
        g.Balls.Clear();
        g.Idle(100);
        Assert.Single(g.Balls);
    }

    [Fact]
    public void There_are_never_more_fireballs_than_castles_standing()
    {
        var g = Game();
        for (int i = 0; i < 4; i++) g.PutBall(Field.CentreX + i * 20, Field.CentreY, 200, 100);
        g.FellForTest(3);
        Assert.Equal(3, g.Balls.Count(b => !b.Dead));
        g.FellForTest(2);
        Assert.Equal(2, g.Balls.Count(b => !b.Dead));
    }

    [Fact]
    public void Fireballs_never_settle_into_going_straight_across()
    {
        var g = Game();
        var b = g.PutBall(Field.CentreX, Field.CentreY, 400, 0);
        g.Idle(120);
        Assert.True(Math.Abs(b.Vy) > b.Speed * .05);
    }

    // ───────────────────────────── the computer warlords ─────────────────────────────

    [Fact]
    public void A_computer_warlord_slides_its_shield_to_meet_a_fireball()
    {
        var g = Real();
        g.Balls.Clear();
        var c = g.Castles[2];
        g.PutBallAt(2, 20, 3, -300, 0);
        g.Idle(45);
        Assert.InRange(c.S, 3 - 1.6, 3 + 1.6);
    }

    [Fact]
    public void Flinging_from_the_aimed_place_sends_a_fireball_at_a_rival_s_warlord()
    {
        var g = Game();
        var k = g.Castles[0];
        var rival = g.Castles[2];
        double s = GameEngine.AimAt(k, rival);
        var (u, v) = Castle.PathPoint(s);
        var (px, py) = k.ToWorld(u, v);
        var (kx, ky) = k.King;
        var (rx, ry) = rival.King;
        double a = Math.Atan2(py - ky, px - kx), want = Math.Atan2(ry - ky, rx - kx);
        Assert.True(Math.Abs(a - want) < .06);
    }

    [Fact]
    public void An_extra_life_comes_at_15000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.FirstExtraLife - 50);
        g.FellForTest(1);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── around the game ─────────────────────────────

    [Fact]
    public void The_computer_wins_rounds_at_your_castle()
    {
        var g = new GameEngine(2, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < 60 * 240 && g.State == GameState.Playing && g.Round < 3; t++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Round >= 3, $"round {g.Round}");
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_back()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 25);
        Assert.Contains(g.Castles, c => c.BricksLeft < 33 || !c.Alive);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 25)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        static (int, double, int, int) Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 40; t++) g.Tick(g.Pilot.Decide(g));
            return (g.Score, g.Castles[0].S, g.Castles.Sum(c => c.BricksLeft), g.Round);
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.SetScore(99999);
        g.SetLives(1);
        g.FellForTest(GameEngine.You);
        g.Idle((int)(60 * (GameEngine.LostSeconds + 5)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "LRD") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("LRD", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
