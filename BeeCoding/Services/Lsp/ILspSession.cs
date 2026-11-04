namespace BeeCoding.Services.Lsp;

/// <summary>What <see cref="LspEndpoint"/> needs from a language-server session: JSON messages in and out, and the
/// URI of the one document the browser edits. Either its own clangd process (<see cref="ClangdSession"/>) or a share of
/// one common process (<see cref="SharedClangd"/>).</summary>
public interface ILspSession : IAsyncDisposable
{
    string MainFileUri { get; }
    string WorkspaceDir { get; }
    event Func<string, Task>? MessageReceived;
    event Action? Exited;
    Task SendAsync(string json, CancellationToken ct = default);
}
