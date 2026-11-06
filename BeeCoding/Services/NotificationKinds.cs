namespace BeeCoding.Services;

/// <summary>The kinds of notification, in one place: label for the settings screens, which are emailed by default, and the
/// comma-separated form kept in the database. A kind not listed here is ignored when settings are saved.</summary>
public static class NotificationKinds
{
    public static readonly (string Kind, string Label)[] All =
    [
        ("reaction", "Reactions on my cards"),
        ("comment", "Comments on my cards"),
        ("report", "A problem of mine was reported"),
        ("report-update", "My problem report was answered"),
        ("proposal", "Someone proposed a problem for my board"),
        ("proposal-update", "My problem proposal was answered"),
        ("practice-review", "My practice submission was reviewed"),
        ("review-submitted", "A problem is waiting for review (admins)"),
        ("org-invite", "I am invited to an organization"),
        ("group-opens", "A session (folder) of a board I am in opens"),
        ("group-closing", "A session of a board I am in closes within the hour"),
        ("problem-new", "A new problem on a board I am in"),
        ("lti-sync-failed", "Sending grades to the LMS failed (teachers)"),
        ("security", "Security: password or two-step verification changed"),
    ];

    /// <summary>Reactions and comments are frequent, so they are not emailed unless an admin allows it.</summary>
    public static readonly string[] DefaultEmail = ["report", "report-update", "proposal", "proposal-update", "practice-review", "review-submitted", "org-invite", "group-closing", "lti-sync-failed", "security"];

    public static bool IsKnown(string kind) => All.Any(k => k.Kind == kind);

    public static HashSet<string> Parse(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(IsKnown).ToHashSet();

    public static string? Join(IEnumerable<string> kinds)
    {
        var set = kinds.Where(IsKnown).ToHashSet();
        return set.Count == 0 ? null : string.Join(',', All.Select(k => k.Kind).Where(set.Contains));
    }
}
