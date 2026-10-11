namespace StellarAscension;

/// <summary>Pointy-top hex grid in "odd-r" offset coordinates (odd rows shifted right), like Civ VI's map.
/// The map wraps east-west, as Civ VI's standard maps do.</summary>
public static class Hex
{
    static readonly (int dx, int dy)[] EvenRow = { (1, 0), (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1) };
    static readonly (int dx, int dy)[] OddRow = { (1, 0), (1, -1), (0, -1), (-1, 0), (0, 1), (1, 1) };

    public static IEnumerable<(int x, int y)> Neighbors(int x, int y, int w, int h)
    {
        var d = (y & 1) == 0 ? EvenRow : OddRow;
        foreach (var (dx, dy) in d)
        {
            int nx = x + dx, ny = y + dy;
            if (ny < 0 || ny >= h) continue;
            yield return ((nx % w + w) % w, ny);
        }
    }

    static (int q, int r) ToAxial(int x, int y) => (x - (y - (y & 1)) / 2, y);

    /// <summary>Hex distance, taking the east-west wrap into account.</summary>
    public static int Distance(int x1, int y1, int x2, int y2, int w)
    {
        int best = int.MaxValue;
        foreach (var shift in new[] { 0, w, -w })
        {
            var (q1, r1) = ToAxial(x1, y1);
            var (q2, r2) = ToAxial(x2 + shift, y2);
            int dq = q1 - q2, dr = r1 - r2;
            best = Math.Min(best, (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2);
        }
        return best;
    }

    public static IEnumerable<(int x, int y)> Within(int x, int y, int radius, int w, int h)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            int ny = y + dy;
            if (ny < 0 || ny >= h) continue;
            for (int dx = -radius - 1; dx <= radius + 1; dx++)
            {
                int nx = ((x + dx) % w + w) % w;
                if (Distance(x, y, nx, ny, w) <= radius) yield return (nx, ny);
            }
        }
    }
}
