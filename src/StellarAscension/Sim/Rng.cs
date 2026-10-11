namespace StellarAscension;

/// <summary>A small seeded random generator whose whole state is one number, so a saved game continues exactly.</summary>
public sealed class Rng
{
    public ulong State { get; set; }

    public Rng() { }
    public Rng(ulong seed) { State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }

    public ulong Next()
    {
        // splitmix64
        ulong z = State += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public int Int(int maxExclusive) => maxExclusive <= 1 ? 0 : (int)(Next() % (ulong)maxExclusive);
    public int Range(int min, int maxInclusive) => min + Int(maxInclusive - min + 1);
    public double Double() => (Next() >> 11) * (1.0 / (1UL << 53));
    public bool Chance(double p) => Double() < p;

    public T Pick<T>(IReadOnlyList<T> list) => list[Int(list.Count)];

    public T Weighted<T>(IReadOnlyList<T> items, Func<T, double> weight)
    {
        double total = 0;
        foreach (var i in items) total += Math.Max(0, weight(i));
        if (total <= 0) return items[Int(items.Count)];
        double r = Double() * total;
        foreach (var i in items)
        {
            r -= Math.Max(0, weight(i));
            if (r < 0) return i;
        }
        return items[^1];
    }

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Int(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
