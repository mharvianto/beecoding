namespace BeeCoding.Services.Similarity;

/// <summary>
/// Cosine similarity over TF-IDF weighted character 4-grams — no model, no dependency. Measured on this bank: finds a
/// problem rewritten with the same words almost always, a rewrite in other words often not, so it only ranks candidates
/// for a human to look at (it does not decide duplicates).
/// </summary>
public static class TextSimilarity
{
    /// <summary>Score of <paramref name="query"/> against each document, 0..1 (higher = more alike). Builds a corpus per call — for tests and one-offs; the app uses <see cref="TextCorpusCache"/>.</summary>
    public static double[] Scores(string query, IReadOnlyList<string> docs)
        => new TextCorpus(docs.Select((d, i) => (i, d)).ToList()).Scores(query, Enumerable.Range(0, docs.Count).ToList());

    internal static Dictionary<string, int> Grams(string text)
    {
        const int N = 4;
        var s = string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var m = new Dictionary<string, int>();
        for (int i = 0; i + N <= s.Length; i++) { var g = s.Substring(i, N); m[g] = m.GetValueOrDefault(g) + 1; }
        return m;
    }
}

/// <summary>The weighted 4-gram vectors of a set of documents, built once and queried many times.</summary>
public sealed class TextCorpus
{
    private readonly Dictionary<int, int> _index = new();
    private readonly Dictionary<string, int> _df = new();
    private readonly List<Dictionary<string, double>> _weights = [];
    private readonly int _count;

    public TextCorpus(IReadOnlyList<(int Id, string Text)> docs)
    {
        _count = docs.Count;
        var grams = docs.Select(d => TextSimilarity.Grams(d.Text)).ToList();
        foreach (var g in grams) foreach (var k in g.Keys) _df[k] = _df.GetValueOrDefault(k) + 1;
        for (int i = 0; i < docs.Count; i++) { _index[docs[i].Id] = i; _weights.Add(Weigh(grams[i])); }
    }

    private Dictionary<string, double> Weigh(Dictionary<string, int> g)
    {
        var w = new Dictionary<string, double>();
        double norm = 0;
        foreach (var (k, c) in g)
        {
            if (!_df.TryGetValue(k, out var d)) continue;   // a gram no document has says nothing about similarity
            var x = (1 + Math.Log(c)) * Math.Log(1 + (double)_count / d);
            w[k] = x; norm += x * x;
        }
        norm = Math.Sqrt(norm);
        if (norm > 0) foreach (var k in w.Keys.ToList()) w[k] /= norm;
        return w;
    }

    /// <summary>Score against the documents with these ids; an id the corpus does not hold (added since it was built) scores 0.</summary>
    public double[] Scores(string query, IReadOnlyList<int> ids)
    {
        var q = Weigh(TextSimilarity.Grams(query));
        return ids.Select(id =>
        {
            if (!_index.TryGetValue(id, out var i)) return 0.0;
            var d = _weights[i];
            var (small, big) = d.Count < q.Count ? (d, q) : (q, d);
            double dot = 0;
            foreach (var (k, v) in small) if (big.TryGetValue(k, out var o)) dot += v * o;
            return dot;
        }).ToArray();
    }
}

/// <summary>
/// Keeps one <see cref="TextCorpus"/> of every public or pending problem for a few minutes, so a class finishing the same
/// problem at once does not each rebuild it (about 0.1 s of CPU per 450 problems). shortcut: a problem added or edited in the
/// last <see cref="Ttl"/> is missing or old in it (the lists are hints for a person); shorten the TTL or key it on a version
/// counter if that starts to matter.
/// </summary>
public sealed class TextCorpusCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (TextCorpus Corpus, DateTime At)? _value;

    public async Task<TextCorpus> GetAsync(Func<Task<List<(int Id, string Text)>>> load)
    {
        if (_value is { } v && DateTime.UtcNow - v.At < Ttl) return v.Corpus;
        await _gate.WaitAsync();
        try
        {
            if (_value is { } w && DateTime.UtcNow - w.At < Ttl) return w.Corpus;
            var corpus = new TextCorpus(await load());
            _value = (corpus, DateTime.UtcNow);
            return corpus;
        }
        finally { _gate.Release(); }
    }
}
