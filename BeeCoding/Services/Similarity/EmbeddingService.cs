using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace BeeCoding.Services.Similarity;

public class EmbeddingOptions
{
    /// <summary>Folder holding <c>model_quantized.onnx</c> and <c>sentencepiece.bpe.model</c> of multilingual-e5-small (see INSTALL.md). Empty = off.</summary>
    public string ModelDir { get; set; } = "";
    /// <summary>The model (~120 MB on disk, a few hundred MB in RAM) is dropped after this long without a request.</summary>
    public int IdleUnloadSeconds { get; set; } = 120;
    /// <summary>How often the backfill looks for bank problems without a current embedding.</summary>
    public int BackfillMinutes { get; set; } = 10;
}

/// <summary>
/// multilingual-e5-small through ONNX Runtime, loaded on first use and released when idle — the RAM is only held while an
/// admin is looking at similar problems or the backfill is running. One request at a time (the session is shared).
/// </summary>
public sealed class EmbeddingService(IOptions<EmbeddingOptions> options, ILogger<EmbeddingService> log) : IDisposable
{
    public const int Dim = 384;
    private const int MaxTokens = 512;
    private readonly EmbeddingOptions _opt = options.Value;
    private readonly object _gate = new();
    private InferenceSession? _session;
    private SentencePieceTokenizer? _tokenizer;
    private Timer? _idle;
    private DateTime _lastUsed;

    private string ModelFile => Path.Combine(_opt.ModelDir, "model_quantized.onnx");
    private string TokenizerFile => Path.Combine(_opt.ModelDir, "sentencepiece.bpe.model");
    public bool Enabled => !string.IsNullOrWhiteSpace(_opt.ModelDir) && File.Exists(ModelFile) && File.Exists(TokenizerFile);

    /// <summary>A different model in the folder (e5-base is 768 wide) must fail loudly: reading its first 384 numbers would store plausible but wrong vectors.</summary>
    public static void RequireDim(int actual)
    {
        if (actual != Dim) throw new InvalidOperationException($"The model in Embeddings:ModelDir produces {actual}-dimensional vectors; this build expects multilingual-e5-small ({Dim}).");
    }

    /// <summary>Identifies what an embedding was made from, so a changed text recomputes it. The "e5s1" prefix names the model: change it whenever another model (even another 384-wide one) is installed, or old and new vectors would be mixed.</summary>
    public static string HashOf(string text) => "e5s1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];

    /// <summary>XLM-R ids from the sentencepiece ones: this tokenizer starts with &lt;s&gt; = 1 and has no &lt;/s&gt;, and the model's
    /// vocabulary is shifted by one (&lt;s&gt; 0, &lt;pad&gt; 1, &lt;/s&gt; 2, pieces +1).</summary>
    public static long[] ToModelIds(IReadOnlyList<int> sentencePieceIds)
    {
        var body = sentencePieceIds.Skip(1).Take(MaxTokens - 2).Select(i => (long)i + 1);
        return [0, .. body, 2];
    }

    /// <summary>Unit-length embedding of <paramref name="text"/> (e5 expects the "query: " prefix for symmetric similarity).</summary>
    public float[] Embed(string text)
    {
        lock (_gate)
        {
            if (!Enabled) throw new InvalidOperationException("Embeddings are not configured.");
            _idle?.Dispose();
            try
            {
                if (_session is null)
                {
                    using var s = File.OpenRead(TokenizerFile);
                    _tokenizer = SentencePieceTokenizer.Create(s);
                    var so = new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 2 };
                    _session = new InferenceSession(ModelFile, so);
                    log.LogInformation("Embedding model loaded");
                }
                var ids = ToModelIds(_tokenizer!.EncodeToIds("query: " + (text.Length > 4000 ? text[..4000] : text)));
                var shape = new[] { 1, ids.Length };
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, shape)),
                    NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), shape)),
                };
                if (_session.InputMetadata.ContainsKey("token_type_ids"))
                    inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[ids.Length], shape)));
                using var run = _session.Run(inputs);
                var hidden = run.First().AsTensor<float>();   // [1, tokens, 384]
                RequireDim(hidden.Dimensions[2]);
                var v = new float[Dim];
                for (int t = 0; t < ids.Length; t++) for (int d = 0; d < Dim; d++) v[d] += hidden[0, t, d];
                double norm = 0;
                for (int d = 0; d < Dim; d++) { v[d] /= ids.Length; norm += v[d] * v[d]; }
                norm = Math.Sqrt(norm);
                if (norm > 0) for (int d = 0; d < Dim; d++) v[d] = (float)(v[d] / norm);
                return v;
            }
            finally
            {
                _lastUsed = DateTime.UtcNow;
                _idle = new Timer(_ => Unload(), null, IdleAfter, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private TimeSpan IdleAfter => TimeSpan.FromSeconds(Math.Max(10, _opt.IdleUnloadSeconds));

    private void Unload()
    {
        lock (_gate)
        {
            // A timer that was already waiting for the lock while a request ran must not drop the model just used; the newer timer will.
            if (_session is null || DateTime.UtcNow - _lastUsed < IdleAfter - TimeSpan.FromSeconds(1)) return;
            _session?.Dispose(); _session = null; _tokenizer = null;
            _idle?.Dispose(); _idle = null;
            log.LogInformation("Embedding model released");
        }
    }

    public static byte[] ToBytes(float[] v) { var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length); return b; }
    public static float[] FromBytes(byte[] b) { var v = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, v, 0, b.Length); return v; }
    public static double Dot(float[] a, float[] b) { double s = 0; for (int i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }

    public void Dispose() { lock (_gate) { _session?.Dispose(); _idle?.Dispose(); } }
}
