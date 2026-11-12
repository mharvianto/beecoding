namespace BeeCoding.Services.Ai;

public class AiOptions
{
    /// <summary>Master switch. Also needs <see cref="ApiKey"/> to be non-empty.</summary>
    public bool Enabled { get; set; }

    /// <summary>Bearer token for the OpenAI-compatible chat-completions endpoint.</summary>
    public string ApiKey { get; set; } = "";

    public string BaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1/chat/completions";
    public string Model { get; set; } = "nvidia/nemotron-3.5-bolt-e4b";

    /// <summary>Model for the heavier one-shot JSON tasks (generate-problem, next-problem
    /// pick). Falls back to <see cref="Model"/> when empty. Point this at a fast model if the
    /// default is slow to write a whole problem.</summary>
    public string GenerateModel { get; set; } = "";

    public double Temperature { get; set; } = 0.3;
    public int MaxTokens { get; set; } = 700;
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Hard ceiling for <c>POST /api/ai/generate-problem</c>. The call streams, so
    /// as long as tokens keep arriving (see <see cref="GenerateIdleTimeoutSeconds"/>) it will
    /// run up to this long before giving up.</summary>
    public int GenerateTimeoutSeconds { get; set; } = 300;

    /// <summary>Abort generation if the model sends nothing for this many seconds. A slow but
    /// steady model finishes; a stalled one fails fast.</summary>
    public int GenerateIdleTimeoutSeconds { get; set; } = 45;

    /// <summary>Idle window for the admin AI check / report triage. These ask the model to actually solve the problem,
    /// so the first byte can take much longer than when it merely writes one.</summary>
    public int CheckIdleTimeoutSeconds { get; set; } = 120;

    /// <summary>Pass <c>chat_template_kwargs.thinking</c> to the model (slower, more thorough).</summary>
    public bool Thinking { get; set; }

    /// <summary>OpenAI-style <c>reasoning_effort</c> for reasoning models (gpt-oss, o-series):
    /// "low" | "medium" | "high". Empty = don't send it. The JSON tasks force "low" so the
    /// model doesn't burn its budget on a chain-of-thought preamble.</summary>
    public string ReasoningEffort { get; set; } = "";

    /// <summary>Reply language when the request doesn't specify one: "id" or "en".</summary>
    public string DefaultReplyLanguage { get; set; } = "id";

    /// <summary>Minimum seconds between a user's hint requests.</summary>
    public int RateLimitSeconds { get; set; } = 8;

    /// <summary>Backup providers tried in order when the main one fails (timeout, 429, 5xx, 401/403, empty reply).
    /// A person's own organization key never falls back to these (that would spend the platform's money).</summary>
    public List<AiFallback> Fallbacks { get; set; } = [];

    /// <summary>Student code is truncated to this many chars before being sent.</summary>
    public int MaxCodeChars { get; set; } = 16_000;
}

public class AiFallback
{
    public string ApiKey { get; set; } = "";
    /// <summary>Blank = the main BaseUrl.</summary>
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
}
