using System.ComponentModel.DataAnnotations;

namespace BeeCoding.Models;

public enum UserRole { Teacher, Student }

public enum ProblemLevel { Easy = 1, Medium = 2, Hard = 3 }

public enum MembershipRole { Owner, Teacher, Student }

/// <summary>Entities whose public URL uses an unguessable slug instead of the int Id.</summary>
public interface IHasSlug
{
    string Slug { get; set; }
}

public enum SubmissionStatus { Queued, Running, Done }

public enum Verdict
{
    None,
    Accepted,
    WrongAnswer,
    TimeLimit,
    MemoryLimit,
    RuntimeError,
    CompileError
}

public class User
{
    public int Id { get; set; }

    [MaxLength(256)]
    public string Email { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    /// <summary>False for accounts created through "Sign in with Google": <see cref="PasswordHash"/> is
    /// then a random value nobody knows, until the user sets a password themselves.</summary>
    public bool HasPassword { get; set; } = true;

    /// <summary>When the user proved they own <see cref="Email"/> (by following an emailed link, or an
    /// admin / LMS vouching for it). Null = not verified. Only enforced when outgoing email is
    /// configured and, for a hard gate, <c>Auth:RequireVerifiedEmail</c> is on.</summary>
    public DateTime? EmailVerifiedAt { get; set; }

    /// <summary>Base32 secret of the user's authenticator app (TOTP, RFC 6238). Set at the start of
    /// setup but only counts once <see cref="TotpEnabledAt"/> is set (after a first valid code).</summary>
    [MaxLength(64)]
    public string? TotpSecret { get; set; }

    /// <summary>When the authenticator app was confirmed. Null = no TOTP second factor.</summary>
    public DateTime? TotpEnabledAt { get; set; }

    /// <summary>When the user turned on "email me a code" as a second factor. Null = off.</summary>
    public DateTime? EmailMfaEnabledAt { get; set; }

    /// <summary>Time step (unix seconds / 30) of the last accepted TOTP code, so a code can't be replayed.</summary>
    public long TotpLastStep { get; set; }

    [MaxLength(120)]
    public string DisplayName { get; set; } = "";

    public UserRole Role { get; set; }

    /// <summary>Admin granted from the admin panel, as opposed to the <c>Admin:Emails</c>
    /// config list (which always wins and can't be revoked from the UI).</summary>
    public bool IsAdmin { get; set; }

    /// <summary>Total experience points earned by solving problems (see <see cref="SolveRecord"/>).</summary>
    public int Xp { get; set; }

    /// <summary>Consecutive local-calendar days (client-reported, not server UTC) with at
    /// least one Accepted practice solve. See ProgressService.UpdateStreakAsync.</summary>
    public int CurrentStreak { get; set; }

    /// <summary>Last local day that counted toward <see cref="CurrentStreak"/>.</summary>
    public DateOnly? StreakLocalDay { get; set; }

    /// <summary>Highest <see cref="CurrentStreak"/> ever reached — a personal-best record
    /// that survives a broken streak (CurrentStreak itself resets to 1 on a missed day).</summary>
    public int LongestStreak { get; set; }

    /// <summary>Most Accepted solves (board + practice combined, see
    /// ProgressService.CountSolvedOnLocalDayAsync) ever recorded on a single local day.</summary>
    public int MaxSolvedInADay { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-delete marker (admin-only action). Not a global query filter — the
    /// row keeps showing up via existing relations (submissions, posts, ...) so past
    /// activity still renders correctly; enforced explicitly at login and active-session
    /// checks, and in the admin user list. See <c>SoftDelete.CanRestore</c> for the
    /// 30-second undo window.</summary>
    public DateTime? DeletedAt { get; set; }

    public List<BoardMembership> Memberships { get; set; } = new();
}

public class Board
{
    public int Id { get; set; }

    [MaxLength(160)]
    public string Title { get; set; } = "";

    /// <summary>Human-typed code to join the board (short, rotatable).</summary>
    [MaxLength(12)]
    public string JoinCode { get; set; } = "";

    /// <summary>Unguessable public identifier used in URLs (the int Id stays internal).</summary>
    [MaxLength(24)]
    public string Slug { get; set; } = "";

    public int OwnerId { get; set; }
    public User? Owner { get; set; }

    /// <summary>Which organization (if any) this class belongs to — null for a board with
    /// no institutional affiliation. Scopes Org Admin visibility and (via the board) which
    /// organization's AI settings apply. Set automatically for a board auto-created by an
    /// LTI launch (from the platform's own OrganizationId); otherwise chosen by the owner
    /// at creation time if they belong to more than one organization.</summary>
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    /// <summary>Exam mode for the board's UNGROUPED problems (a grouped problem uses its
    /// group's own <see cref="ProblemGroup.ExamMode"/>): students never see peers' answers/progress.</summary>
    public bool ExamMode { get; set; }

    /// <summary>Lecturing / live-coding mode: the teacher's editor buffer is streamed to
    /// students (read-only) so they can follow along; students still code, run and ask the AI.</summary>
    public bool LecturingMode { get; set; }

    /// <summary>Deter casual copying/screenshots of problem statements (select/copy blocked,
    /// blur-on-leave, name watermark). Cannot truly stop a camera — makes leaks attributable.</summary>
    public bool ProtectContent { get; set; }

    /// <summary>Comma-separated, lowercase tags for the owner's own organizing/filtering
    /// (e.g. by class, semester, cohort) — purely descriptive, no access-control meaning.</summary>
    [MaxLength(300)]
    public string Tags { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-delete marker — excluded from all normal queries via a global query
    /// filter (see AppDbContext). Restorable within <c>SoftDelete.UndoWindow</c>.</summary>
    public DateTime? DeletedAt { get; set; }

    public List<BoardMembership> Members { get; set; } = new();
    public List<Problem> Problems { get; set; } = new();
    public List<ProblemGroup> Groups { get; set; } = new();
}

/// <summary>A one-level folder/session that groups a board's problems. Carries its own
/// hide flag, exam mode and open/close window. A problem with no group falls back to the
/// board's own <see cref="Board.ExamMode"/> and is always "open".</summary>
public class ProblemGroup
{
    public int Id { get; set; }

    public int BoardId { get; set; }
    public Board? Board { get; set; }

    [MaxLength(120)]
    public string Title { get; set; } = "";

    public int Position { get; set; }

    /// <summary>Teacher-hidden: every problem in the group is hidden from students.</summary>
    public bool Hidden { get; set; }

    /// <summary>Exam mode for this group's problems: students never see peers' work on them.</summary>
    public bool ExamMode { get; set; }

    /// <summary>UTC. Before this the group is invisible to students. Null = open from the start.</summary>
    public DateTime? OpensAt { get; set; }

    /// <summary>UTC. From this instant students can still read the problems but not submit.
    /// Null = never closes.</summary>
    public DateTime? ClosesAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Problem> Problems { get; set; } = new();
}

public class BoardMembership
{
    public int Id { get; set; }

    public int BoardId { get; set; }
    public Board? Board { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public MembershipRole Role { get; set; }

    /// <summary>Feature 5 (per-student): teacher hides this student's cells from other students.</summary>
    public bool HiddenByTeacher { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public class Problem : IHasSlug
{
    public int Id { get; set; }

    /// <summary>Unguessable public identifier used in URLs (the int Id stays internal).
    /// Assigned automatically on insert (see <c>AppDbContext.SaveChangesAsync</c>).</summary>
    [MaxLength(16)]
    public string Slug { get; set; } = "";

    public int BoardId { get; set; }
    public Board? Board { get; set; }

    /// <summary>Optional folder/session this problem belongs to (null = ungrouped).</summary>
    public int? GroupId { get; set; }
    public ProblemGroup? Group { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    public string StatementMarkdown { get; set; } = "";

    /// <summary>Comma-separated list of languages a submission may use ("c", "cpp").
    /// Empty = every supported language is allowed.</summary>
    [MaxLength(32)]
    public string AllowedLanguages { get; set; } = "";

    /// <summary>Comma-separated, lowercase topic tags, e.g. "array,graph,dp".</summary>
    [MaxLength(300)]
    public string Tags { get; set; } = "";

    public ProblemLevel Level { get; set; } = ProblemLevel.Medium;

    /// <summary>True when this problem was written by the AI generator.</summary>
    public bool GeneratedByAi { get; set; }

    /// <summary>Comma-separated header names a submission may NOT #include, e.g.
    /// "algorithm,numeric". When set, umbrella headers (bits/stdc++.h) are also blocked.</summary>
    [MaxLength(300)]
    public string? BannedHeaders { get; set; }

    /// <summary>Comma-separated identifiers a submission may not use, e.g. "std::sort,qsort".</summary>
    [MaxLength(300)]
    public string? BannedSymbols { get; set; }

    /// <summary>When set, a submission's stdin content is written to this filename in the
    /// sandbox work directory instead of being piped to stdin — for problems that require
    /// fopen()-style file I/O. Null = stdin (the default). See InputFilePolicy.</summary>
    [MaxLength(64)]
    public string? InputFileName { get; set; }

    public int TimeLimitMs { get; set; } = 1000;

    public int MemoryLimitKb { get; set; } = 32768;

    public int Position { get; set; }

    /// <summary>Teacher-hidden: excluded from the student-facing problem list/wall and
    /// can't be opened or submitted to by a student, while staff still see and can edit it.
    /// For a draft/not-ready problem — distinct from <see cref="DeletedAt"/>, which removes
    /// it for everyone including staff.</summary>
    public bool Hidden { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Provenance when this problem was copied in from the bank.</summary>
    public int? SourceBankProblemId { get; set; }

    /// <summary>Soft-delete marker — excluded from all normal queries via a global query
    /// filter (see AppDbContext). Restorable within <c>SoftDelete.UndoWindow</c>.</summary>
    public DateTime? DeletedAt { get; set; }

    public List<TestCase> TestCases { get; set; } = new();
}

/// <summary>
/// A reusable problem in a teacher's private bank. Adding one to a board COPIES it into
/// <see cref="Problem"/> — the board copy is independent afterwards.
/// </summary>
public class BankProblem : IHasSlug
{
    public int Id { get; set; }

    /// <summary>Unguessable public identifier used in URLs (the int Id stays internal).
    /// Assigned automatically on insert (see <c>AppDbContext.SaveChangesAsync</c>).</summary>
    [MaxLength(16)]
    public string Slug { get; set; } = "";

    public int OwnerId { get; set; }
    public User? Owner { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    public string StatementMarkdown { get; set; } = "";

    /// <summary>Comma-separated list of languages a submission may use ("c", "cpp").
    /// Empty = every supported language is allowed.</summary>
    [MaxLength(32)]
    public string AllowedLanguages { get; set; } = "";

    /// <summary>True when this problem was written by the AI generator.</summary>
    public bool GeneratedByAi { get; set; }

    /// <summary>An AI-generated problem starts here — kept out of the public/shared listing
    /// (IsPublic stays false) until an admin reviews it. See AdminUiController's AI review
    /// queue. Never set for a teacher-authored problem.</summary>
    public bool PendingReview { get; set; }

    /// <summary>Comma-separated header names a submission may NOT #include (see Problem).</summary>
    [MaxLength(300)]
    public string? BannedHeaders { get; set; }

    /// <summary>Comma-separated identifiers a submission may not use, e.g. "std::sort,qsort".</summary>
    [MaxLength(300)]
    public string? BannedSymbols { get; set; }

    /// <summary>When set, a submission's stdin content is written to this filename in the
    /// sandbox work directory instead of being piped to stdin (see Problem.InputFileName).</summary>
    [MaxLength(64)]
    public string? InputFileName { get; set; }

    public int TimeLimitMs { get; set; } = 1000;

    public int MemoryLimitKb { get; set; } = 32_768;

    public ProblemLevel Level { get; set; } = ProblemLevel.Medium;

    /// <summary>Comma-separated, lowercase topic tags.</summary>
    [MaxLength(300)]
    public string Tags { get; set; } = "";

    /// <summary>Other teachers may browse and copy it (hidden tests are never sent to them).</summary>
    public bool IsPublic { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-delete marker — excluded from all normal queries via a global query
    /// filter (see AppDbContext). Restorable within <c>SoftDelete.UndoWindow</c>.</summary>
    public DateTime? DeletedAt { get; set; }

    public List<BankTestCase> TestCases { get; set; } = new();
}

public class BankTestCase
{
    public int Id { get; set; }

    public int BankProblemId { get; set; }
    public BankProblem? BankProblem { get; set; }

    public string Stdin { get; set; } = "";
    public string ExpectedStdout { get; set; } = "";
    public bool IsSample { get; set; }
    public int Points { get; set; } = 1;
    public int Position { get; set; }
}

public class TestCase
{
    public int Id { get; set; }

    public int ProblemId { get; set; }
    public Problem? Problem { get; set; }

    public string Stdin { get; set; } = "";

    public string ExpectedStdout { get; set; } = "";

    public bool IsSample { get; set; }

    public int Points { get; set; } = 1;

    public int Position { get; set; }
}

public class Submission
{
    public int Id { get; set; }

    public int ProblemId { get; set; }
    public Problem? Problem { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public string Code { get; set; } = "";

    /// <summary>"c" or "cpp" the student chose to compile with; null = the problem's language.</summary>
    [MaxLength(8)]
    public string? Language { get; set; }

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Queued;

    public Verdict Verdict { get; set; } = Verdict.None;

    public int RuntimeMs { get; set; }

    public int MemoryKb { get; set; }

    /// <summary>0..1 fraction of testcase points passed.</summary>
    public double Score { get; set; }

    /// <summary>Feature 4: student hides their own answer from other students.</summary>
    public bool HiddenByStudent { get; set; }

    public string CompilerOutput { get; set; } = "";

    /// <summary>"Test N of M / input / expected / your output" for the first testcase that
    /// didn't pass — null on Accepted or CompileError. Staff-only once served over the API
    /// (see Mapping.ToDto), since it can reveal a hidden test's expected output.</summary>
    public string? FailedTest { get; set; }

    /// <summary>XP this specific submission earned — only >0 the one time a problem is
    /// newly, fully solved (see ProgressService.AwardSolveAsync). Lets the client tell a
    /// genuine first-time solve apart from a repeat Accepted, so it can celebrate even when
    /// discovered on reload/reopen rather than via the live SignalR push.</summary>
    public int XpAwarded { get; set; }

    /// <summary>Client's local calendar day at submit time — see BankSubmission.LocalDay
    /// (same field, same purpose: daily-streak/solved-today bucketing by the student's
    /// local day, not server UTC). Null for submissions made before this existed.</summary>
    public DateOnly? LocalDay { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? JudgedAt { get; set; }
}

/// <summary>
/// A student's living "post" on the board wall for one problem. Persists across
/// resubmissions (always shows the latest submission) and carries the social bits.
/// </summary>
public class Post
{
    public int Id { get; set; }

    public int BoardId { get; set; }
    public Board? Board { get; set; }

    public int ProblemId { get; set; }
    public Problem? Problem { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Free-text caption the author can add ("stuck on test 3").</summary>
    public string Note { get; set; } = "";

    /// <summary>Student hides this problem's work (live draft + submitted card) from peers.</summary>
    public bool HiddenByStudent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the author last acknowledged feedback on this post. Reactions and
    /// comments by others newer than this show up to the author as "new" on the wall.</summary>
    public DateTime ActivitySeenAt { get; set; } = DateTime.UtcNow;

    public List<PostReaction> Reactions { get; set; } = new();
    public List<PostComment> Comments { get; set; } = new();
}

public class PostReaction
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public Post? Post { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(16)]
    public string Emoji { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PostComment
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public Post? Post { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(2000)]
    public string Body { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A sign-in identity from an external provider (Google), tied to a local account. Matched on
/// (<see cref="Provider"/>, <see cref="Subject"/>) — the provider's stable user id — never on email.</summary>
public class ExternalLogin
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(32)]
    public string Provider { get; set; } = "";

    [MaxLength(255)]
    public string Subject { get; set; } = "";

    /// <summary>The provider's email for this identity when it was linked (display only).</summary>
    [MaxLength(256)]
    public string Email { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>A 6-digit code emailed to the user as a second factor (or to prove the email works when turning
/// the factor on). Only a hash is stored; it expires quickly and burns after too many wrong guesses.</summary>
public class EmailLoginCode
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(64)]
    public string CodeHash { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public int Attempts { get; set; }
}

/// <summary>A single-use backup code for signing in when the second-factor device is lost. Only the
/// SHA-256 of the code is stored; the plain codes are shown once when generated.</summary>
public class MfaRecoveryCode
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(64)]
    public string CodeHash { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }
}

/// <summary>A registered WebAuthn credential (passkey / security key) used as a second factor.</summary>
public class UserPasskey
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>The authenticator's credential id, unique across all users.</summary>
    [MaxLength(1024)]
    public byte[] CredentialId { get; set; } = Array.Empty<byte>();

    /// <summary>COSE-encoded public key.</summary>
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();

    public long SignCount { get; set; }

    /// <summary>Label the user gave the device ("iPhone", "YubiKey").</summary>
    [MaxLength(80)]
    public string Name { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>A single-use, time-limited password-reset link. Only the SHA-256 of the token is
/// stored — the raw token exists only in the emailed (or admin-issued) link.</summary>
public class PasswordResetToken
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(64)]
    public string TokenHash { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    /// <summary>True when the link was delivered by email — following it proves the user owns
    /// the address, so redeeming it also verifies their email. Admin-issued links don't.</summary>
    public bool ProvesEmail { get; set; }
}

/// <summary>A single-use link emailed to confirm an address. Like reset tokens, only the hash is stored.
/// <see cref="Email"/> is the address it was sent to, so changing the account's email voids it.</summary>
public class EmailVerificationToken
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(256)]
    public string Email { get; set; } = "";

    [MaxLength(64)]
    public string TokenHash { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}

/// <summary>An in-app notification for a wall-post author: someone reacted to or commented on
/// their card. Reactions on the same post coalesce into one unread row (see NotificationService)
/// so a burst of emoji doesn't flood the bell.</summary>
public class Notification
{
    public int Id { get; set; }

    /// <summary>Recipient.</summary>
    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>"reaction" or "comment".</summary>
    [MaxLength(16)]
    public string Kind { get; set; } = "";

    public int BoardId { get; set; }
    public int ProblemId { get; set; }
    public int PostId { get; set; }
    public int? CommentId { get; set; }

    public int ActorUserId { get; set; }
    [MaxLength(120)]
    public string ActorName { get; set; } = "";
    public bool ActorIsStaff { get; set; }

    /// <summary>Latest emoji, for a reaction notification.</summary>
    [MaxLength(16)]
    public string? Emoji { get; set; }

    /// <summary>First part of the comment, for a comment notification.</summary>
    [MaxLength(200)]
    public string? Snippet { get; set; }

    /// <summary>How many reactions this (coalesced) row stands for.</summary>
    public int Count { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}

/// <summary>One row per distinct problem a user has fully solved (first Accepted).
/// De-dupes XP: a bank problem and its board copies share the same <see cref="ProblemKey"/>.</summary>
public class SolveRecord
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>"bank:{bankProblemId}" for bank/board-copied problems, "board:{problemId}" otherwise.</summary>
    [MaxLength(40)]
    public string ProblemKey { get; set; } = "";

    public ProblemLevel Level { get; set; }

    public int XpAwarded { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A student's submission to a bank problem in practice mode (independent of any board).</summary>
public class BankSubmission
{
    public int Id { get; set; }

    public int BankProblemId { get; set; }
    public BankProblem? BankProblem { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public string Code { get; set; } = "";

    /// <summary>"c" or "cpp" the student chose to compile with; null = the problem's language.</summary>
    [MaxLength(8)]
    public string? Language { get; set; }

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Queued;
    public Verdict Verdict { get; set; } = Verdict.None;
    public int RuntimeMs { get; set; }
    public int MemoryKb { get; set; }
    public double Score { get; set; }
    public string CompilerOutput { get; set; } = "";

    /// <summary>"Test N of M / input / expected / your output" for the first testcase that
    /// didn't pass — null on Accepted or CompileError. Staff-only once served over the API
    /// (see Mapping.ToDto), same as Submission.FailedTest — even the student whose attempt
    /// this is shouldn't see a hidden test's expected output.</summary>
    public string? FailedTest { get; set; }

    /// <summary>XP this specific submission earned — see Submission.XpAwarded.</summary>
    public int XpAwarded { get; set; }

    /// <summary>Client's local calendar day at submit time, e.g. "2026-09-16" — used for
    /// daily-streak bucketing, since a streak day means the student's local day, not
    /// server UTC. Null for submissions made before this existed.</summary>
    public DateOnly? LocalDay { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? JudgedAt { get; set; }
}

/// <summary>Per-day rollup of a user's AI usage (hint + recommendation calls).</summary>
public class AiUsage
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>UTC calendar day.</summary>
    public DateOnly Day { get; set; }

    public int Calls { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
}

/// <summary>Singleton row (Id=1) of admin-tunable AI runtime settings — global pause
/// ("kill switch") and the default daily request quota per role. See AiRuntimeSettings
/// for the in-memory cache this backs.</summary>
public class AiSettings
{
    // No "= 1" default: that was fine while this was a true singleton, but now every
    // organization gets its own row too (see OrganizationId below) — Program.cs sets Id=1
    // explicitly for the one platform-default row it seeds; every other row auto-increments.
    public int Id { get; set; }

    /// <summary>Null = the platform-wide default (always row Id=1 — seeded at startup, see
    /// Program.cs). A non-null value is one organization's own override, editable only by
    /// that org's Org Admin (or a super admin) — see OrgAdminController. An organization
    /// with no row here just uses the platform default.</summary>
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    /// <summary>An org's own pause only stops that org's AI usage; the platform default row's
    /// Paused is the one true kill switch — it wins even over an org that isn't paused.</summary>
    public bool Paused { get; set; }

    [MaxLength(300)]
    public string? PausedReason { get; set; }

    public int DailyQuotaStudent { get; set; } = 20;
    public int DailyQuotaTeacher { get; set; } = 50;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Which AI provider/credential to bill — layered the same way as AiSettings (Id=1 row is
/// the platform-wide default, OrganizationId != null rows are one org's own override), but
/// kept as its own table since this is about WHICH account is billed, not usage limits.
/// Any field left null/empty falls through to the next layer: org row -> platform DB row ->
/// appsettings.json's Ai:* (so a fresh deployment with no rows here still works unchanged).
/// See AiProviderRuntime for the in-memory cache and AiTutorService.Effective() for the
/// actual per-request resolution.
/// </summary>
public class AiProviderConfig
{
    public int Id { get; set; }

    /// <summary>Null = the platform-wide default row. Non-null = one organization's own
    /// override, editable by that org's Org Admin (or a super admin).</summary>
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    /// <summary>Bearer token for the OpenAI-compatible endpoint. Never returned to a client
    /// once saved — the admin/org-admin API exposes only whether one is set.</summary>
    [MaxLength(300)]
    public string? ApiKey { get; set; }

    [MaxLength(500)]
    public string? BaseUrl { get; set; }

    [MaxLength(200)]
    public string? Model { get; set; }

    [MaxLength(200)]
    public string? GenerateModel { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Per-user AI override: a custom daily quota and/or an outright ban. No row for a
/// user means "use the role default from AiSettings, not banned".</summary>
public class AiUserSetting
{
    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Null = use the role default.</summary>
    public int? DailyQuotaOverride { get; set; }

    public bool Banned { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// How many times a user has asked the AI tutor for a hint on one problem recently.
/// Drives progressive hints: the more they ask, the more the tutor reveals (still never
/// the full solution). Resets after a quiet gap.
/// </summary>
public class AiHintProgress
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>"bank:{id}" or "board:{id}".</summary>
    [MaxLength(40)]
    public string ProblemKey { get; set; } = "";

    public int Count { get; set; }

    /// <summary>Short hash of the code at the last hint — used to tell whether the student
    /// tried something between asks (if so, the tutor doesn't escalate).</summary>
    [MaxLength(32)]
    public string? LastCodeHash { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Trail of moderation actions (soft-delete, restore, permanent purge) for the admin panel.
/// Actor/target are denormalized (email, label) so the row still reads sensibly after the
/// actor or target account is itself deleted or purged.
/// </summary>
public class AuditLogEntry
{
    public int Id { get; set; }

    public int ActorUserId { get; set; }

    [MaxLength(256)]
    public string ActorEmail { get; set; } = "";

    /// <summary>"delete" | "restore" | "purge".</summary>
    [MaxLength(20)]
    public string Action { get; set; } = "";

    /// <summary>"User" | "Board" | "Problem" | "BankProblem".</summary>
    [MaxLength(20)]
    public string TargetType { get; set; } = "";

    public int TargetId { get; set; }

    /// <summary>Title/email/display name at the time of the action, for a readable log
    /// even once the target row is purged.</summary>
    [MaxLength(300)]
    public string TargetLabel { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Platform-wide runtime overrides for the handful of judge/LSP knobs that are actually
/// safe to flip without a restart — the rest of JudgeOptions/LspOptions (queue backend,
/// concurrency limits sized into a semaphore at startup, RequireSandbox's fail-fast check)
/// stays appsettings.json-only since those are process-topology decisions, not live-tunable
/// values. Single row, Id fixed at 1 — seeded at startup from whatever appsettings.json
/// already says (see Program.cs), then edited from /admin/reports from then on.
/// </summary>
public class PlatformRuntimeSettings
{
    public int Id { get; set; }

    /// <summary>Master switch for the clangd bridge (completion/hover/format). Checked live
    /// on every /api/lsp/enabled call and every /lsp/cpp connection attempt.</summary>
    public bool LspEnabled { get; set; }

    /// <summary>Minimum spacing (ms) between a user's run/submit requests — see RateLimiter.</summary>
    public int JudgeRateLimitMs { get; set; } = 1500;

    /// <summary>Two-step verification policy: accounts in these groups must have an authenticator app or
    /// email code turned on before they can use the app (see MfaRequirementGate). Off by default.</summary>
    public bool MfaRequireAdmin { get; set; }
    public bool MfaRequireOrgAdmin { get; set; }
    public bool MfaRequireTeacher { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
