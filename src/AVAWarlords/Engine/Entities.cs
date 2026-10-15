namespace AVAWarlords.Engine;

/// <summary>The stick (any way round slides the shield) and the catch button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>
/// The battlefield: the screen below the score bar, a castle in each corner and the dragon in the middle.
/// Each castle is laid out on its own grid of cells counted from its corner of the screen.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Left = 0, Right = 1024, Top = 64, Bottom = 768;
    public const double CentreX = (Left + Right) / 2, CentreY = (Top + Bottom) / 2;
    /// <summary>The size of a castle cell (a brick), in pixels, and how many cells a castle spans each way.</summary>
    public const double Cell = 26;
    public const int Cells = 8;
}

/// <summary>
/// A castle in a corner: three thick L-shaped walls of bricks round the warlord, and a shield that slides
/// round the outside along an L-shaped path. Positions inside it are in cells (u across, v up from the corner,
/// whichever corner it is). The shield's place on its path runs from 0 (at one edge of the screen) round the
/// outer corner (at 7.5) to 15 (at the other edge).
/// </summary>
public sealed class Castle
{
    /// <summary>The walls: three layers, cells whose larger coordinate is 4, 5 or 6.</summary>
    public const int WallFrom = 4, WallTo = 6;
    /// <summary>The warlord sits in the cells nearer the corner than this.</summary>
    public const double KingSize = 3;
    /// <summary>The shield's lane, its half-length and half-thickness, all in cells.</summary>
    public const double Lane = 7.5, Half = 1.0, Thick = .3, PathLength = 15;

    public int Index;
    public string Name = "";
    public uint Colour;
    public double Ox, Oy;
    public int Sx, Sy;
    public readonly bool[,] Bricks = new bool[Field.Cells, Field.Cells];
    public bool Alive = true;
    /// <summary>Where the shield is along its path.</summary>
    public double S = Lane;

    // The computer warlord's state.
    public double AimError, Hold;
    public bool CatchNext;
    public int Target = -1;

    public Castle()
    {
        Rebuild();
    }

    public void Rebuild()
    {
        for (int i = 0; i < Field.Cells; i++)
            for (int j = 0; j < Field.Cells; j++)
                Bricks[i, j] = Math.Max(i, j) is >= WallFrom and <= WallTo;
        Alive = true;
        S = Lane;
    }

    public int BricksLeft => Bricks.Cast<bool>().Count(b => b);

    public (double U, double V) ToLocal(double x, double y) => ((x - Ox) * Sx / Field.Cell, (y - Oy) * Sy / Field.Cell);
    public (double X, double Y) ToWorld(double u, double v) => (Ox + u * Field.Cell * Sx, Oy + v * Field.Cell * Sy);

    /// <summary>A point on the shield's path.</summary>
    public static (double U, double V) PathPoint(double s) => s <= Lane ? (Lane, s) : (PathLength - s, Lane);

    /// <summary>The place on the path nearest a point (in cells).</summary>
    public static double PathAt(double u, double v) => u >= v ? Math.Clamp(v, 0, Lane) : PathLength - Math.Clamp(u, 0, Lane);

    public (double X, double Y) King => ToWorld(KingSize / 2, KingSize / 2);
}

/// <summary>A fireball from the dragon. While a shield holds it, it rides along with the shield.</summary>
public sealed class Ball
{
    public const double Radius = 7;

    public double X, Y, Vx, Vy, Speed;
    /// <summary>The castle whose shield touched it last (-1 for none yet).</summary>
    public int Owner = -1;
    public Castle? HeldBy;
    public double HeldTime;
    public bool Dead;
}

public enum EffectKind { Score, Burst, Word, Fall, Breath }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.7, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
