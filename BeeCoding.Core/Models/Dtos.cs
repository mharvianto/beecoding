namespace BeeCoding.Models;

// ---- Auth ----
public record RegisterDto(string Email, string Password, string DisplayName, string Role, string? TeacherCode = null);
public record LoginDto(string Email, string Password);
public record MeDto(int Id, string Email, string DisplayName, string Role, bool IsAdmin = false, bool HasOrgAdmin = false);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);
public record UpdateProfileDto(string DisplayName);
public record DeleteAccountDto(string Password, bool DeleteOwnedBoards = false);

// ---- Boards ----
public record CreateBoardDto(string Title, int? OrganizationId = null);
public record JoinBoardDto(string Code);
public record UpdateBoardDto(bool? ExamMode, bool? ProtectContent, bool? LecturingMode);
public record BoardDto(int Id, string Slug, string Title, string JoinCode, bool ExamMode, bool ProtectContent, bool LecturingMode, bool IsOwner, string Role, int MemberCount, int ProblemCount, int? OrganizationId = null, string? OrganizationName = null);

public record MemberDto(int UserId, string DisplayName, string Role, bool HiddenByTeacher);
public record UpdateMemberDto(bool HiddenByTeacher);

// ---- Problems ----
public record TestCaseDto(int Id, string Stdin, string ExpectedStdout, bool IsSample, int Points, int Position);
public record UpsertTestCaseDto(int? Id, string Stdin, string ExpectedStdout, bool IsSample, int Points, int Position);
public record UpsertProblemDto(
    string Title,
    string StatementMarkdown,
    string AllowedLanguages,             // csv of "c"/"cpp"; empty => all languages allowed
    int TimeLimitMs,
    int MemoryLimitKb,
    int Position,
    string? Tags,
    string? Level,
    List<UpsertTestCaseDto>? TestCases,   // null => leave test cases untouched
    string? BannedHeaders = null,         // comma-separated headers a submission may not #include
    string? BannedSymbols = null,         // comma-separated identifiers it may not use
    string? InputFileName = null);        // set => stdin is written to this file instead of piped

/// <summary>Full problem view for the board owner/teacher.</summary>
public record ProblemDto(
    int Id, string Slug, int BoardId, string Title, string StatementMarkdown, string AllowedLanguages,
    int TimeLimitMs, int MemoryLimitKb, int Position,
    string Tags, string Level, bool GeneratedByAi,
    List<TestCaseDto> TestCases, string? BannedHeaders, string? BannedSymbols, string? InputFileName = null);

/// <summary>Problem view for a student: only sample tests exposed.</summary>
public record StudentProblemDto(
    int Id, string Slug, int BoardId, string Title, string StatementMarkdown, string AllowedLanguages,
    int TimeLimitMs, int MemoryLimitKb, int Position,
    string Tags, string Level, bool GeneratedByAi,
    List<TestCaseDto> SampleTests, string? BannedHeaders, string? BannedSymbols, string? InputFileName = null);

// ---- Problem bank ----
public record BankSummaryDto(
    int Id, string Slug, string Title, string AllowedLanguages, string Level, string Tags, bool IsPublic,
    bool Mine, string OwnerName, int TestCount, int SampleCount, DateTime UpdatedAt, bool GeneratedByAi,
    bool PendingReview);

public record BankProblemDto(
    int Id, string Slug, string Title, string StatementMarkdown, string AllowedLanguages,
    int TimeLimitMs, int MemoryLimitKb, string Level, string Tags, bool IsPublic, bool GeneratedByAi,
    bool PendingReview, bool Mine, string OwnerName, DateTime UpdatedAt,
    List<TestCaseDto> TestCases, string? BannedHeaders, string? BannedSymbols, string? InputFileName = null);   // full set only for the owner; samples only otherwise

public record UpsertBankProblemDto(
    string Title, string StatementMarkdown, string AllowedLanguages,
    int TimeLimitMs, int MemoryLimitKb, string? Level, string Tags, bool IsPublic,
    List<UpsertTestCaseDto>? TestCases,   // null => leave test cases untouched
    string? BannedHeaders = null,
    string? BannedSymbols = null,
    string? InputFileName = null);

// ---- Admin ingest (token-authed, for scripting the problem bank) ----
public record AdminTestInput(string Stdin, string ExpectedStdout, bool? IsSample, int? Points, int? Position);
public record AdminBankProblemInput(
    string Title,
    string StatementMarkdown,
    string? AllowedLanguages,  // csv of "c"/"cpp"; empty/null => all languages
    string? Level,             // Easy | Medium | Hard  (default Medium)
    string? Tags,
    int? TimeLimitMs,
    int? MemoryLimitKb,
    bool? IsPublic,            // default true
    List<AdminTestInput>? Tests,
    string? BannedHeaders = null,
    string? BannedSymbols = null,
    string? InputFileName = null);
public record AdminIngestDto(
    string? OwnerEmail,        // an existing Teacher; default = first teacher
    bool? ReplaceExisting,     // default true: upsert by (owner, title)
    List<AdminBankProblemInput> Problems);
public record AdminBankRow(int Id, string Title, string AllowedLanguages, string Level, string Tags,
    bool IsPublic, int TestCount, int SampleCount, DateTime UpdatedAt);
public record AdminIngestResultDto(
    List<AdminBankRow> Created, List<AdminBankRow> Updated, List<string> Errors);

// ---- Admin panel (cookie-authed, Admin:Emails) ----
public record AdminUserRow(int Id, string Email, string DisplayName, string Role, bool IsAdmin,
    int Xp, DateTime CreatedAt, int OwnedBoards, int Submissions);
public record AdminUserPageDto(List<AdminUserRow> Rows, int Total, int Page, int PageSize);

public record AdminAiUsageBucket(int Calls, long PromptTokens, long CompletionTokens, long TotalTokens);
public record AdminAiUsageRow(int UserId, string Email, string DisplayName,
    AdminAiUsageBucket Today, AdminAiUsageBucket Month, AdminAiUsageBucket AllTime);
public record AdminAiUsagePageDto(List<AdminAiUsageRow> Rows, int Total, int Page, int PageSize);

public record AdminProblemTest(string Stdin, string ExpectedStdout, bool IsSample, int Points, int Position);
public record AdminProblemItem(
    string OwnerEmail, string Title, string StatementMarkdown, string AllowedLanguages, string Level,
    string Tags, int TimeLimitMs, int MemoryLimitKb, bool IsPublic, bool GeneratedByAi,
    string? BannedHeaders, string? BannedSymbols, List<AdminProblemTest> Tests, string? InputFileName = null);
public record AdminProblemBundle(int Version, DateTime ExportedAt, List<AdminProblemItem> Problems);
public record AdminImportResult(int Created, int Updated, int Skipped, List<string> Errors);

// ---- Admin: browse all boards ----
public record AdminBoardRow(int Id, string Slug, string Title, string OwnerEmail, string OwnerName,
    int MemberCount, int ProblemCount, DateTime CreatedAt);

// ---- Admin: trash (soft-deleted rows; admin restore/purge has no time limit) ----
public record AdminTrashUserRow(int Id, string Email, string DisplayName, DateTime DeletedAt);
public record AdminTrashBoardRow(string Slug, string Title, string OwnerEmail, DateTime DeletedAt);
public record AdminTrashProblemRow(string Slug, string Title, string BoardSlug, string BoardTitle, DateTime DeletedAt);
public record AdminTrashBankRow(string Slug, string Title, string OwnerEmail, DateTime DeletedAt);
public record AdminTrashBulkDto(List<string> Ids);
public record AdminTrashBulkResult(int Count, List<string> Errors);

/// <summary>Generic paged-list envelope — shared by trash and the audit log.</summary>
public record AdminPageDto<T>(List<T> Rows, int Total, int Page, int PageSize);

// ---- Admin: audit log ----
public record AdminAuditLogRow(int Id, DateTime CreatedAt, string ActorEmail, string Action,
    string TargetType, int TargetId, string TargetLabel);

// ---- Admin: all board submissions, platform-wide (/admin/submissions) ----
public record AdminSubmissionRow(int Id, DateTime CreatedAt, string Verdict, double Score,
    int RuntimeMs, int MemoryKb, string? Language,
    int UserId, string UserEmail, string UserDisplayName,
    string ProblemTitle, string BoardSlug, string BoardTitle);

// ---- Admin: dashboard (overview landing tab) ----
public record AdminDashboardDto(
    int TotalUsers, int TeacherCount, int StudentCount, int AdminCount,
    int TotalBoards, int TotalProblems, int TotalBankProblems,
    int TotalSubmissions, int AcceptedSubmissions,
    int PendingAiReview, int TrashCount,
    AdminAiUsageBucket AiToday, AdminAiUsageBucket AiMonth,
    List<AdminAuditLogRow> RecentActivity);

// ---- Admin: dashboard charts ----
public record AdminWeeklyStatDto(string WeekStart, int ActiveUsers, int Submissions);
public record AdminTopicStatDto(string Tag, int Attempts, int Accepted, double AcceptRate);

// ---- Admin: dashboard engagement/AI-usage charts with a selectable granularity ----
// PeriodStart is "yyyy-MM-dd" for week/day, or a full ISO datetime (UTC, on the hour) for hour.
public record AdminEngagementPointDto(string PeriodStart, int ActiveUsers, int Submissions);
public record AdminAiEngagementPointDto(string PeriodStart, int Calls, long TotalTokens);

// ---- Student: personal dashboard, aggregated across every board + practice/bank ----
public record StudentDashboardDto(
    int Xp, int Level, int LevelStartXp, int NextLevelXp, int SolvedCount,
    int Rank, int RankedUsers, int BoardsJoined, int TotalAttempts, int AcceptedAttempts);
public record MyWeeklyStatDto(string WeekStart, int Attempts, int Solved);
public record MyEngagementPointDto(string PeriodStart, int Attempts, int Solved);

// ---- Teacher: one board's own stats (Board.vue staff panel) ----
public record BoardStatsDto(
    int TotalStudents, int TotalProblems, int TotalSubmissions, int AcceptedSubmissions,
    List<AdminWeeklyStatDto> Weekly, List<AdminTopicStatDto> Topics);

// ---- Admin: role / admin-flag management ----
public record AdminChangeRoleDto(string Role);   // "Teacher" | "Student"

// ---- Admin: bulk user delete ----
public record AdminBulkDeleteUsersDto(List<int> Ids);
public record AdminBulkDeleteUsersResult(int Deleted, List<string> Errors);

// ---- Admin: bulk board archive ----
public record AdminBulkArchiveDto(List<string> Slugs);
public record AdminBulkArchiveResult(int Archived, List<string> Errors);

// ---- Admin: system status ----
public record AdminSystemStatusDto(
    string SandboxMode, bool BwrapUsable, bool SandboxRequired,
    string JudgeQueueBackend, string RealtimeBackend,
    bool RedisConfigured, bool? RedisConnected,
    long? PendingJudgeJobs, bool DatabaseOk, DateTime CheckedAt,
    string GccVersion, string GppVersion);

// ---- Admin: AI-generated problem review queue ----
public record AdminAiReviewTest(string Stdin, string ExpectedStdout, bool IsSample);
public record AdminAiReviewRow(
    string Slug, string Title, string OwnerEmail, string OwnerName, string Level, string Tags,
    string AllowedLanguages, string StatementMarkdown, int TimeLimitMs, int MemoryLimitKb,
    List<AdminAiReviewTest> Tests, DateTime CreatedAt);
public record AdminAiReviewActionDto(string? Reason);

// ---- Admin: AI kill-switch, quotas, per-user overrides ----
public record AiGlobalSettingsDto(bool Paused, string? PausedReason, int DailyQuotaStudent, int DailyQuotaTeacher);
public record AiUserOverrideDto(int UserId, string Email, string DisplayName, int? DailyQuotaOverride, bool Banned);
public record AiSetUserOverrideDto(int? DailyQuotaOverride, bool Banned);

// ---- AI: which provider/credential to bill (platform default, or one org's own) ----
// ApiKey is never returned once saved — HasApiKey + a short preview only. BaseUrl/Model/
// GenerateModel round-trip in plain text (not secret); empty/null clears that field's
// override (falls through to the next layer — see AiProviderRuntime).
public record AiProviderConfigDto(bool HasApiKey, string? ApiKeyPreview, string? BaseUrl, string? Model, string? GenerateModel);
public record AiSetProviderConfigDto(string? ApiKey, string? BaseUrl, string? Model, string? GenerateModel);

// ---- Admin: bulk user import (CSV) ----
public record AdminUserImportRequest(string Csv, string? BoardSlug, string? DefaultRole);
public record AdminUserImportRow(
    string Email, string DisplayName, string Role, bool Created, bool AddedToBoard,
    string? GeneratedPassword, string? Error);
public record AdminUserImportResult(int Created, int Existing, int Errors, List<AdminUserImportRow> Rows);

// ---- Practice (students solve bank problems) ----
public record PracticeSummaryDto(
    int Id, string Slug, string Title, string AllowedLanguages, string Level, string Tags,
    string MyVerdict, double MyBestScore, bool Solved);

public record PracticePageDto(
    int Total, int Solved, int Page, int PageSize, List<PracticeSummaryDto> Items);

public record TopicProgressDto(string Tag, int Total, int Solved, int Attempted);
public record RecommendationDto(int Id, string Slug, string Title, string AllowedLanguages, string Level, string Tags, string Reason);
public record PracticeGuideDto(
    List<TopicProgressDto> Topics, List<RecommendationDto> Recommended,
    string Source, bool AiAvailable);   // Source: "heuristic" | "ai"

public record PracticeProblemDto(
    int Id, string Slug, string Title, string StatementMarkdown, string AllowedLanguages,
    int TimeLimitMs, int MemoryLimitKb, string Level, string Tags,
    List<TestCaseDto> SampleTests, bool Solved, string? BannedHeaders, string? BannedSymbols, string? InputFileName = null);

public record BankSubmissionDto(
    int Id, int BankProblemId, string Status, string Verdict,
    int RuntimeMs, int MemoryKb, double Score, string CompilerOutput,
    DateTime CreatedAt, DateTime? JudgedAt, string? Code, string Language);

// ---- Submissions ----
public record SubmitDto(string Code, string? Language = null);   // Language: "c" | "cpp" override
public record UpdateSubmissionDto(bool HiddenByStudent);
public record SubmissionDto(
    int Id, int ProblemId, int UserId, string AuthorName,
    string Status, string Verdict, int RuntimeMs, int MemoryKb, double Score,
    bool HiddenByStudent, bool Mine, string? Code, string CompilerOutput,
    DateTime CreatedAt, DateTime? JudgedAt, string Language,
    string? FailedTest = null);   // staff-only — see Mapping.ToDto

// ---- Live board / progress grid ----
public record ProgressCellDto(
    int UserId, int ProblemId,
    string Verdict,        // "None" if never attempted
    double Score,
    int Attempts,
    bool Redacted,         // true => viewer may only see that an attempt exists
    bool Latest,           // latest submission is Accepted
    DateTime LastAt,       // time of the latest submission
    int? LatestSubmissionId = null);   // null when Redacted — fetch GET /api/submissions/{id} for full code

public record ProgressBoardDto(
    int BoardId,
    bool ExamMode,
    bool ViewerIsStaff,
    List<MemberDto> Students,
    List<ProblemSummaryDto> Problems,
    List<ProgressCellDto> Cells);

public record ProblemSummaryDto(int Id, string Slug, string Title, int Position, int TimeLimitMs, int MemoryLimitKb, string AllowedLanguages, string Tags, string Level);

// ---- Padlet-style wall ----
public record ReactionDto(string Emoji, int Count, bool Mine);
public record CommentDto(int Id, int UserId, string AuthorName, string Body, DateTime CreatedAt, bool CanDelete);

public record WallPostDto(
    int PostId,
    int ProblemId,
    int UserId,
    string AuthorName,
    string Note,
    bool Mine,
    bool Redacted,            // peer post hidden -> show only that it exists
    bool HiddenByStudent,     // author's own "hide from peers" state (for the toggle)
    string Verdict,           // "None" if not yet judged / no submission
    double Score,
    int Attempts,
    int RuntimeMs,
    int MemoryKb,
    string Language,
    string? CodePreview,      // first lines of latest submission, null when not visible
    int? LatestSubmissionId,  // fetch GET /api/submissions/{id} for the full, untruncated code
    DateTime UpdatedAt,
    List<ReactionDto> Reactions,
    List<CommentDto> Comments);

public record WallDto(
    int BoardId,
    bool ExamMode,
    bool ViewerIsStaff,
    List<ProblemSummaryDto> Problems,
    List<WallPostDto> Posts);

public record NoteDto(string Note);
public record ReactDto(string Emoji);
public record CommentBodyDto(string Body);

// ---- LTI 1.3 (Learning Tools Interoperability) — admin platform registry ----
public record AdminLtiPlatformDto(
    int Id, string Name, string Issuer, string ClientId, string DeploymentIds,
    string AuthLoginUrl, string AuthTokenUrl, string JwksUrl, bool Enabled, DateTime CreatedAt,
    int? OrganizationId, string? OrganizationName);
public record AdminUpsertLtiPlatformDto(
    string Name, string Issuer, string ClientId, string DeploymentIds,
    string AuthLoginUrl, string AuthTokenUrl, string JwksUrl, bool Enabled, int? OrganizationId = null);
/// <summary>Values an LMS admin needs to register BeeCoding as an external tool —
/// shown in the admin LTI tab so they can copy them in.</summary>
public record AdminLtiToolConfigDto(
    string LoginInitiationUrl, string LaunchUrl, string JwksUrl, string DeepLinkingUrl);

// ---- LTI: deep-linking picker (teacher, mid-launch from the LMS) ----
public record LtiDeepLinkContextDto(string PlatformName, bool AcceptsResourceLink);
public record LtiDeepLinkSelectDto(string Token, string? BoardSlug = null, string? BankProblemSlug = null);
public record LtiDeepLinkResultDto(string ReturnUrl, string Jwt);

// ---- Organizations (multi-tenant: separate universities/institutions) ----
public record OrganizationDto(int Id, string Name, string Slug, DateTime CreatedAt);
public record AdminUpsertOrganizationDto(string Name, string Slug);
public record OrgSummaryDto(int MemberCount, int BoardCount);
public record OrgMemberRow(int UserId, string Email, string DisplayName, string OrgRole, DateTime JoinedAt);
public record OrgAddMemberDto(string Email, string OrgRole);   // "Member" | "Admin"
public record OrgSetMemberRoleDto(string OrgRole);
public record OrgBoardRow(int Id, string Slug, string Title, string OwnerEmail, int MemberCount, int ProblemCount, DateTime CreatedAt);

// ---- Org Admin: bulk member add via CSV (existing accounts only — no signup here) ----
public record OrgMemberImportDto(string Csv);
public record OrgMemberImportRow(string Email, string OrgRole, bool Added, string? Message);
public record OrgMemberImportResult(int Added, int Skipped, int Errors, List<OrgMemberImportRow> Rows);

// ---- Org Admin: bulk-create boards, all owned by one existing Teacher, all in this org ----
public record OrgBulkCreateBoardsDto(string OwnerEmail, List<string> Titles);
public record OrgBulkCreateBoardsRow(string Title, bool Created, string? Slug, string? Message);
public record OrgBulkCreateBoardsResult(int Created, int Errors, List<OrgBulkCreateBoardsRow> Rows);
public record OrgAiSettingsDto(bool Paused, string? PausedReason, int DailyQuotaStudent, int DailyQuotaTeacher);
public record OrgDashboardDto(
    int TotalMembers, int TeacherCount, int StudentCount, int AdminCount,
    int TotalBoards, int TotalProblems, int TotalSubmissions, int AcceptedSubmissions,
    AdminAiUsageBucket AiToday, AdminAiUsageBucket AiMonth);

// ---- Ad-hoc run ----
public record RunDto(string Language, string Code, string Stdin,
    int? ProblemId = null, int? BankProblemId = null);   // for per-problem header restrictions
public record RunResultDto(
    bool CompileOk, string CompilerOutput,
    string Stdout, string Stderr,
    int RuntimeMs, int MemoryKb,
    bool TimedOut, int ExitCode, int Signal);
