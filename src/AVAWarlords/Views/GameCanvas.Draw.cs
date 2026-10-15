using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAWarlords.Engine;
using Effect = AVAWarlords.Engine.Effect;

namespace AVAWarlords.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAWarlords/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF4EEE0, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFFA8A090, Fire = 0xFFFF8A2A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    /// <summary>A colour darkened (k below 1) or lightened towards white (k above 1).</summary>
    private static uint Shade(uint c, double k)
    {
        double r = (c >> 16) & 255, gg = (c >> 8) & 255, b = c & 255;
        if (k <= 1) { r *= k; gg *= k; b *= k; }
        else { double t = k - 1; r += (255 - r) * t; gg += (255 - gg) * t; b += (255 - b) * t; }
        return 0xFF000000 | (uint)Math.Clamp(r, 0, 255) << 16 | (uint)Math.Clamp(gg, 0, 255) << 8 | (uint)Math.Clamp(b, 0, 255);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        DrawFloor(dc);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame)
        {
            DrawDragon(dc, new Point(Field.CentreX, Field.CentreY), 1);
            foreach (var c in g.Castles) DrawCastle(dc, c);
            foreach (var b in g.Balls) DrawFireball(dc, b);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.CentreX, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.CentreX, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.CentreX, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.CentreX, Field.Height - 18), 26, Yellow, center: true);
    }

    /// <summary>A flagstone floor, lit warm in the middle by the dragon's glow.</summary>
    private void DrawFloor(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0xFF14100E), new Rect(0, 0, Field.Width, Field.Height));
        for (int r = 0; r < 12; r++)
            for (int c = 0; c < 16; c++)
            {
                double x = c * 64 + (r % 2) * 32, y = Field.Top + r * 60;
                uint shade = 0xFF1C1714 + (Hash(c, r, 2) % 6) * 0x020202;
                dc.DrawRectangle(Brush(shade), Pen(0xFF0C0A08, 2), new Rect(x - 32, y, 64, 60));
            }
        double glow = .1 + .04 * Math.Sin(_clock * 3);
        for (int i = 6; i >= 1; i--)
            dc.DrawEllipse(Brush(Alpha(0xFFFF7A2A, glow / i)), null, new Point(Field.CentreX, Field.CentreY), 60 + i * 50, 50 + i * 40);
    }

    // ───────────────────────────── the castles ─────────────────────────────

    private Point W(Castle c, double u, double v) { var (x, y) = c.ToWorld(u, v); return new Point(x, y); }

    private Rect CellRect(Castle c, double u0, double v0, double u1, double v1)
    {
        var a = W(c, u0, v0);
        var b = W(c, u1, v1);
        return new Rect(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    }

    private void DrawCastle(DrawingContext dc, Castle c)
    {
        uint col = c.Alive ? c.Colour : Shade(c.Colour, .45);
        // The courtyard floor inside the walls.
        dc.FillRectangle(Brush(Shade(col, .18)), CellRect(c, 0, 0, Castle.WallFrom, Castle.WallFrom));
        // The bricks, each with a little light on top and shadow below.
        for (int i = 0; i < Field.Cells; i++)
            for (int j = 0; j < Field.Cells; j++)
            {
                if (!c.Bricks[i, j]) continue;
                var r = CellRect(c, i, j, i + 1, j + 1).Deflate(1);
                int layer = Math.Max(i, j) - Castle.WallFrom;
                uint brick = Shade(col, .55 + layer * .15 + (Hash(i, j, c.Index) % 5) * .03);
                dc.FillRectangle(Brush(brick), r, 2);
                dc.DrawLine(Pen(Shade(brick, 1.35), 1.5), r.TopLeft + new Vector(2, 1), r.TopRight + new Vector(-2, 1));
                dc.DrawLine(Pen(Shade(brick, .6), 1.5), r.BottomLeft + new Vector(2, -1), r.BottomRight + new Vector(-2, -1));
            }
        // The warlord on his throne, or what's left of him.
        var (kx, ky) = c.King;
        DrawWarlord(dc, new Point(kx, ky), c.Colour, c.Alive, 1);
        // The shield, as a thick bar along its path.
        if (c.Alive)
        {
            var pts = new List<Point>();
            for (double s = c.S - Castle.Half; s <= c.S + Castle.Half + .01; s += .1)
            {
                var (u, v) = Castle.PathPoint(Math.Clamp(s, 0, Castle.PathLength));
                pts.Add(W(c, u, v));
            }
            for (int i = 0; i < pts.Count - 1; i++) dc.DrawLine(Pen(Alpha(c.Colour, .35), 18), pts[i], pts[i + 1]);
            for (int i = 0; i < pts.Count - 1; i++) dc.DrawLine(Pen(Shade(c.Colour, 1.2), 11), pts[i], pts[i + 1]);
            for (int i = 0; i < pts.Count - 1; i++) dc.DrawLine(Pen(Shade(c.Colour, 1.7), 3), pts[i], pts[i + 1]);
        }
        // The name, out from the corner along the lane.
        var label = W(c, Castle.Lane + 2.5, .55);
        Text(dc, c.Name, label, 20, c.Alive ? c.Colour : 0xFF5A524A, center: true);
    }

    /// <summary>A warlord: crowned, robed in his colours. A fallen one is a toppled crown in the rubble.</summary>
    private void DrawWarlord(DrawingContext dc, Point at, uint colour, bool alive, double scale)
    {
        double k = scale;
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        if (!alive)
        {
            for (int i = 0; i < 6; i++)
            {
                var p = P(-16 + Hash(i, 1, 9) % 32, -6 + Hash(i, 2, 9) % 22);
                Poly(dc, 0xFF4A4038, 0xFF2A2420, 1, p, new Point(p.X + 8, p.Y + 2), new Point(p.X + 5, p.Y + 8));
            }
            Poly(dc, 0xFFB08A20, 0xFF6A5010, 1, P(-8, 6), P(6, 2), P(8, -4), P(2, -2), P(0, -8), P(-3, -1), P(-9, -2));
            double smoke = (_clock * .6) % 1;
            dc.DrawEllipse(Brush(Alpha(0xFF8A8078, .3 * (1 - smoke))), null, P(0, -10 - smoke * 30), 8 + smoke * 10, 6 + smoke * 8);
            return;
        }
        // Robe, arms, head, beard and crown.
        Poly(dc, colour, Shade(colour, .5), 1.5, P(-15, 22), P(-9, -2), P(9, -2), P(15, 22));
        Poly(dc, Shade(colour, 1.4), null, 0, P(-3, -2), P(3, -2), P(4, 22), P(-4, 22));
        dc.DrawEllipse(Brush(0xFFF0C8A0), Pen(0xFF8A5A30, 1), P(0, -9), 7 * k, 7.5 * k);
        Poly(dc, 0xFFE8E0D0, null, 0, P(-6, -7), P(6, -7), P(0, 3));
        dc.DrawEllipse(Brush(0xFF202020), null, P(-2.6, -10), 1.2 * k, 1.2 * k);
        dc.DrawEllipse(Brush(0xFF202020), null, P(2.6, -10), 1.2 * k, 1.2 * k);
        Poly(dc, 0xFFFFD040, 0xFF8A6A10, 1, P(-8, -14), P(-8, -21), P(-4, -17), P(0, -23), P(4, -17), P(8, -21), P(8, -14));
        dc.DrawEllipse(Brush(0xFFE02A2A), null, P(0, -17), 1.6 * k, 1.6 * k);
    }

    // ───────────────────────────── the dragon and the fireballs ─────────────────────────────

    /// <summary>The dragon: coiled in the middle of the field, wings half open, eyes glowing, smoke curling up.</summary>
    private void DrawDragon(DrawingContext dc, Point c, double scale)
    {
        double k = scale, breathe = Math.Sin(_clock * 2);
        Point P(double x, double y) => new(c.X + x * k, c.Y + y * k);
        uint body = 0xFF3A7A3A, dark = 0xFF1A4A1A, belly = 0xFFC8B060;
        // Wings.
        foreach (int s in new[] { -1, 1 })
        {
            double lift = 6 * breathe;
            Poly(dc, 0xFF2A5A2A, dark, 1.5, P(s * 10, -6), P(s * 46, -34 - lift), P(s * 58, -10 - lift), P(s * 44, -2), P(s * 52, 12), P(s * 30, 6), P(s * 14, 12));
            dc.DrawLine(Pen(dark, 1.5), P(s * 10, -6), P(s * 58, -10 - lift));
            dc.DrawLine(Pen(dark, 1.5), P(s * 12, 2), P(s * 52, 12));
        }
        // The tail, curled round.
        for (int i = 0; i < 14; i++)
        {
            double a = i * .32, r = 30 - i * 1.2;
            dc.DrawEllipse(Brush(body), null, P(Math.Cos(a + 1.2) * r, 18 + Math.Sin(a + 1.2) * r * .55), (8 - i * .45) * k, (6 - i * .35) * k);
        }
        // Body and belly.
        dc.DrawEllipse(Brush(body), Pen(dark, 2), P(0, 6), 20 * k, 24 * k);
        dc.DrawEllipse(Brush(belly), null, P(0, 10), 10 * k, 16 * k);
        for (int i = 0; i < 4; i++) dc.DrawLine(Pen(Shade(belly, .7), 1.2), P(-8, 2 + i * 6), P(8, 2 + i * 6));
        // Neck and head, horns, glowing eyes.
        dc.DrawEllipse(Brush(body), Pen(dark, 1.5), P(0, -20), 11 * k, 13 * k);
        Poly(dc, body, dark, 1.5, P(-12, -30), P(12, -30), P(9, -16), P(0, -10), P(-9, -16));
        Poly(dc, 0xFFE0D0A0, null, 0, P(-9, -32), P(-14, -46), P(-5, -34));
        Poly(dc, 0xFFE0D0A0, null, 0, P(9, -32), P(14, -46), P(5, -34));
        double glow = .6 + .4 * Math.Sin(_clock * 5);
        dc.DrawEllipse(Brush(Alpha(0xFFFFE040, glow)), null, P(-5, -25), 3 * k, 2.2 * k);
        dc.DrawEllipse(Brush(Alpha(0xFFFFE040, glow)), null, P(5, -25), 3 * k, 2.2 * k);
        dc.DrawEllipse(Brush(0xFF201000), null, P(-3, -13), 1.5 * k, 1.2 * k);
        dc.DrawEllipse(Brush(0xFF201000), null, P(3, -13), 1.5 * k, 1.2 * k);
        // Smoke.
        for (int i = 0; i < 3; i++)
        {
            double t = (_clock * .5 + i / 3.0) % 1;
            dc.DrawEllipse(Brush(Alpha(0xFF8A8078, .35 * (1 - t))), null, P(Math.Sin(t * 6 + i) * 6, -40 - t * 40), (4 + t * 8) * k, (4 + t * 8) * k);
        }
    }

    private void DrawFireball(DrawingContext dc, Ball b)
    {
        var c = new Point(b.X, b.Y);
        double sp = Math.Max(1, Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy));
        double tx = -b.Vx / sp, ty = -b.Vy / sp;
        if (b.HeldBy != null) (tx, ty) = (0, 0);
        // A flickering tail of flame behind it.
        for (int i = 5; i >= 1; i--)
        {
            double j = Math.Sin(_clock * 40 + i) * 1.5;
            var p = new Point(c.X + tx * i * 5 + ty * j, c.Y + ty * i * 5 - tx * j);
            dc.DrawEllipse(Brush(Alpha(i > 3 ? 0xFFFF3A1A : Fire, .7 - i * .1)), null, p, Ball.Radius - i * .7, Ball.Radius - i * .7);
        }
        dc.DrawEllipse(Brush(Alpha(Fire, .35)), null, c, Ball.Radius + 6, Ball.Radius + 6);
        dc.DrawEllipse(Brush(Fire), null, c, Ball.Radius, Ball.Radius);
        dc.DrawEllipse(Brush(0xFFFFF0A0), null, c, Ball.Radius * .55, Ball.Radius * .55);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4 + Hash(k, (int)e.X, 1) % 10 * .06, d = (4 + 26 * t) * e.Size * 2;
                    dc.FillRectangle(Brush(Alpha(Shade(e.Colour, .8), 1 - t)), new Rect(c.X + Math.Cos(a) * d - 2.5, c.Y + Math.Sin(a) * d - 2.5 + 20 * t * t, 5, 5));
                }
                break;
            case EffectKind.Fall:
            {
                double r = (20 + 80 * Math.Sqrt(t)) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, (1 - t) * .6)), null, c, r * .5, r * .5);
                dc.DrawEllipse(null, Pen(Alpha(Fire, 1 - t), 4), c, r, r);
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2), c, r * .7, r * .7);
                break;
            }
            case EffectKind.Breath:
            {
                // A gout of flame from the dragon's mouth the way the fireball went.
                double a = e.Size, len = 70 * Math.Sin(Math.Min(1, t * 1.5) * Math.PI);
                for (int k = 0; k < 8; k++)
                {
                    double d = len * k / 8, w = 4 + k * 1.6;
                    var p = new Point(c.X + Math.Cos(a) * d, c.Y - 20 + Math.Sin(a) * d);
                    dc.DrawEllipse(Brush(Alpha(k < 3 ? 0xFFFFF0A0 : Fire, (1 - t) * .8)), null, p, w, w);
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 14 - t * 24), 30, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 36, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 48), 30, Dim, right: true);
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused)
        {
            Text(dc, $"ROUND {g.Round}", new Point(Field.CentreX, 36), 38, White, center: true);
            // Crowns for the lives left.
            for (int i = 0; i < Math.Min(g.Lives, 8); i++)
            {
                double x = 180 + i * 30, y = 40;
                Poly(dc, 0xFFFFD040, 0xFF8A6A10, 1, new Point(x - 10, y + 6), new Point(x - 10, y - 6), new Point(x - 5, y - 1), new Point(x, y - 9),
                    new Point(x + 5, y - 1), new Point(x + 10, y - 6), new Point(x + 10, y + 6));
            }
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.CentreX, cy = Field.CentreY + 110;
        var (w, h) = (Math.Min(560, Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60), g.SubMessage != null ? 104.0 : 70.0);
        dc.FillRectangle(Brush(0xC0100806), new Rect(cx - w / 2, cy - 44, w, h), 6);
        dc.DrawRectangle(null, Pen(0x80FFB040, 2), new Rect(cx - w / 2, cy - 44, w, h), 6);
        uint colour = msg.Contains("YOUR CASTLE") ? Red : Yellow;
        Text(dc, msg, new Point(cx, cy - 10), msg.Length > 14 ? 42 : 54, colour, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.CentreX;
        Text(dc, "AVA WARLORDS", new Point(cx, 120), 116, 0xFFFFB040, center: true);
        Text(dc, "AFTER THE 1980 ARCADE CLASSIC", new Point(cx, 190), 28, Dim, center: true);
        DrawDragon(dc, new Point(cx, 290), 1.5);
        DrawWarlord(dc, new Point(cx - 300, 290), 0xFF4AB0FF, true, 1.8);
        DrawWarlord(dc, new Point(cx + 300, 290), 0xFFFF4A4A, true, 1.8);
        Text(dc, "LORD AZURE (YOU)", new Point(cx - 300, 350), 24, 0xFF4AB0FF, center: true);
        Text(dc, "THREE RIVAL LORDS", new Point(cx + 300, 350), 24, 0xFFFF4A4A, center: true);
        string[] lines =
        [
            "THE DRAGON BREATHES FIREBALLS - GUARD YOUR WARLORD WITH YOUR SHIELD",
            "HOLD THE BUTTON TO CATCH ONE, LET GO TO FLING IT AT A RIVAL'S CASTLE",
            "BE THE LAST WARLORD STANDING",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 410 + i * 30), 24, White, center: true);
        Text(dc, "BRICK 10   A RIVAL FALLS 100   BY YOUR HAND 500   VICTORY 1000 x ROUND", new Point(cx, 520), 25, Yellow, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 600), 46, Yellow, center: true);
        Text(dc, "ARROWS: SLIDE THE SHIELD    SPACE: CATCH (HOLD) AND THROW (LET GO)", new Point(cx, 664), 24, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.CentreX;
        Text(dc, "THE GREATEST WARLORDS", new Point(cx, 130), 64, 0xFFFFB040, center: true);
        Text(dc, "RANK NAME    SCORE   ROUND", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4} ", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.CentreX;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
