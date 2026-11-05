namespace BeeCoding.Services.Similarity;

/// <summary>
/// Cosine similarity over TF-IDF weighted character 4-grams — no model, no dependency. Measured on this bank: finds a
/// problem rewritten with the same words almost always, a rewrite in other words often not, so it only ranks candidates
/// for a human to look at (it does not decide duplicates). The corpus is rebuilt per call: fine for an admin clicking a
/// button over a few thousand problems; cache it if that ever changes.
/// </summary>
public static class TextSimilarity
{
    private const int N = 4;

    /// <summary>Score of <paramref name="query"/> against each document, 0..1 (higher = more alike).</summary>
    public static double[] Scores(string query, IReadOnlyList<string> docs)
    {
        var grams = docs.Select(Grams).ToList();
        var df = new Dictionary<string, int>();
        foreach (var g in grams) foreach (var k in g.Keys) df[k] = df.GetValueOrDefault(k) + 1;

        Dictionary<string, double> Weigh(Dictionary<string, int> g)
        {
            var w = new Dictionary<string, double>();
            double norm = 0;
            foreach (var (k, c) in g)
            {
                if (!df.TryGetValue(k, out var d)) continue;   // a gram no document has says nothing about similarity
                var x = (1 + Math.Log(c)) * Math.Log(1 + (double)docs.Count / d);
                w[k] = x; norm += x * x;
            }
            norm = Math.Sqrt(norm);
            if (norm > 0) foreach (var k in w.Keys.ToList()) w[k] /= norm;
            return w;
        }

        var q = Weigh(Grams(query));
        return grams.Select(g =>
        {
            var d = Weigh(g);
            var (small, big) = d.Count < q.Count ? (d, q) : (q, d);
            double dot = 0;
            foreach (var (k, v) in small) if (big.TryGetValue(k, out var o)) dot += v * o;
            return dot;
        }).ToArray();
    }

    private static Dictionary<string, int> Grams(string text)
    {
        var s = string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var m = new Dictionary<string, int>();
        for (int i = 0; i + N <= s.Length; i++) { var g = s.Substring(i, N); m[g] = m.GetValueOrDefault(g) + 1; }
        return m;
    }
}
