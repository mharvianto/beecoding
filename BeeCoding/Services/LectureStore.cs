using System.Collections.Concurrent;

namespace BeeCoding.Services;

public record Lecture(int BoardId, int ProblemId, string Code, string Language, string TeacherName, DateTime UpdatedAt, string Stdin = "", int TeacherId = 0);

/// <summary>
/// Ephemeral store of the teacher's live editor buffer per (board, problem) while lecturing
/// mode is on. Streamed read-only to students. Not durable.
/// </summary>
public interface ILectureStore
{
    ValueTask<Lecture> SetAsync(int boardId, int problemId, string code, string language, string teacherName, string stdin = "", int teacherId = 0);
    ValueTask<Lecture?> GetAsync(int boardId, int problemId);
    ValueTask RemoveForBoardAsync(int boardId);
}

/// <summary>Per-process implementation.</summary>
public sealed class InMemoryLectureStore : ILectureStore
{
    private readonly ConcurrentDictionary<(int boardId, int problemId), Lecture> _lectures = new();

    public ValueTask<Lecture> SetAsync(int boardId, int problemId, string code, string language, string teacherName, string stdin = "", int teacherId = 0)
    {
        var l = new Lecture(boardId, problemId, code, language, teacherName, DateTime.UtcNow, stdin, teacherId);
        _lectures[(boardId, problemId)] = l;
        return ValueTask.FromResult(l);
    }

    public ValueTask<Lecture?> GetAsync(int boardId, int problemId) =>
        ValueTask.FromResult(_lectures.TryGetValue((boardId, problemId), out var l) ? l : null);

    public ValueTask RemoveForBoardAsync(int boardId)
    {
        foreach (var key in _lectures.Where(kv => kv.Value.BoardId == boardId).Select(kv => kv.Key).ToList())
            _lectures.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }
}
