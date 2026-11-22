using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BeeCoding.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiHintEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ProblemKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiHintEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiReviewResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiReviewResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    ActorEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: false),
                    TargetLabel = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankProblemEmbeddings",
                columns: table => new
                {
                    BankProblemId = table.Column<int>(type: "integer", nullable: false),
                    TextHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Vector = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankProblemEmbeddings", x => x.BankProblemId);
                });

            migrationBuilder.CreateTable(
                name: "Journeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Journeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JourneySkips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    BankProblemId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneySkips", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LtiSyncRetries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LtiSyncRetries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LtiToolKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PrivateKeyPem = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LtiToolKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmailDomains = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformRuntimeSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LspEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LspShared = table.Column<bool>(type: "boolean", nullable: false),
                    JudgeRateLimitMs = table.Column<int>(type: "integer", nullable: false),
                    MfaRequireAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    MfaRequireOrgAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    MfaRequireTeacher = table.Column<bool>(type: "boolean", nullable: false),
                    StudentMaxProblems = table.Column<int>(type: "integer", nullable: false),
                    ImageMaxKb = table.Column<int>(type: "integer", nullable: false),
                    ImageMaxPerUser = table.Column<int>(type: "integer", nullable: false),
                    NotificationReadRetentionDays = table.Column<int>(type: "integer", nullable: false),
                    BoardCodeRetentionDays = table.Column<int>(type: "integer", nullable: false),
                    PracticeCodeRetentionDays = table.Column<int>(type: "integer", nullable: false),
                    DeletedUserRetentionDays = table.Column<int>(type: "integer", nullable: false),
                    JourneyEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOffKinds = table.Column<string>(type: "text", nullable: true),
                    NotifyEmailKinds = table.Column<string>(type: "text", nullable: true),
                    NotifyLockedKinds = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformRuntimeSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StatementImages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UnusedSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatementImages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    HasPassword = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TotpSecret = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TotpEnabledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Preferences = table.Column<string>(type: "text", nullable: true),
                    MfaOfferHiddenUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UtcOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    NotifyOffKinds = table.Column<string>(type: "text", nullable: true),
                    NotifyEmailKinds = table.Column<string>(type: "text", nullable: true),
                    EmailMfaEnabledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TotpLastStep = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false),
                    CurrentStreak = table.Column<int>(type: "integer", nullable: false),
                    StreakLocalDay = table.Column<DateOnly>(type: "date", nullable: true),
                    LongestStreak = table.Column<int>(type: "integer", nullable: false),
                    MaxSolvedInADay = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JourneyWorlds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JourneyId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UnlockAfter = table.Column<int>(type: "integer", nullable: false),
                    BossBankProblemId = table.Column<int>(type: "integer", nullable: true),
                    BossXp = table.Column<int>(type: "integer", nullable: false),
                    BadgeName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneyWorlds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JourneyWorlds_Journeys_JourneyId",
                        column: x => x.JourneyId,
                        principalTable: "Journeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiProviderConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    ApiKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GenerateModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MaxTokens = table.Column<int>(type: "integer", nullable: true),
                    ReasoningEffort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ReviewModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    JobEffort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Fallbacks = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiProviderConfigs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    PausedReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DailyQuotaStudent = table.Column<int>(type: "integer", nullable: false),
                    DailyQuotaTeacher = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSettings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LtiPlatforms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Issuer = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeploymentIds = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AuthLoginUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AuthTokenUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    JwksUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LtiPlatforms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LtiPlatforms_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AiHintProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ProblemKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    LastCodeHash = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiHintProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiHintProgresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiUsages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Calls = table.Column<int>(type: "integer", nullable: false),
                    PromptTokens = table.Column<long>(type: "bigint", nullable: false),
                    CompletionTokens = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiUsages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiUserSettings",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    DailyQuotaOverride = table.Column<int>(type: "integer", nullable: true),
                    Banned = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUserSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_AiUserSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankProblems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slug = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StatementMarkdown = table.Column<string>(type: "text", nullable: false),
                    AllowedLanguages = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GeneratedByAi = table.Column<bool>(type: "boolean", nullable: false),
                    PendingReview = table.Column<bool>(type: "boolean", nullable: false),
                    BannedHeaders = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BannedSymbols = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    InputFileName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TimeLimitMs = table.Column<int>(type: "integer", nullable: false),
                    MemoryLimitKb = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    BoardOnly = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RevisionOfId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankProblems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankProblems_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Boards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    JoinCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Slug = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    ExamMode = table.Column<bool>(type: "boolean", nullable: false),
                    LecturingMode = table.Column<bool>(type: "boolean", nullable: false),
                    ProtectContent = table.Column<bool>(type: "boolean", nullable: false),
                    AllowProposals = table.Column<bool>(type: "boolean", nullable: false),
                    Tags = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Boards_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Boards_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailLoginCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLoginCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailLoginCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailVerificationTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailVerificationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailVerificationTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExternalLogins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalLogins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalLogins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfaRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaRecoveryCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BoardId = table.Column<int>(type: "integer", nullable: false),
                    ProblemId = table.Column<int>(type: "integer", nullable: false),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    CommentId = table.Column<int>(type: "integer", nullable: true),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ActorIsStaff = table.Column<bool>(type: "boolean", nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Snippet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReportId = table.Column<int>(type: "integer", nullable: true),
                    ProposalId = table.Column<int>(type: "integer", nullable: true),
                    TargetTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationInvites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    InvitedById = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationInvites_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationInvites_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProvesEmail = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProblemLikes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    BankProblemId = table.Column<int>(type: "integer", nullable: true),
                    ProblemId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemLikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProblemLikes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProblemReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    BankProblemId = table.Column<int>(type: "integer", nullable: true),
                    ProblemId = table.Column<int>(type: "integer", nullable: true),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProblemReports_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SolveRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ProblemKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    XpAwarded = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolveRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SolveRecords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SurveyResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Answers = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyResponses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserBadges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EarnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBadges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBadges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPasskeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignCount = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPasskeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPasskeys_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebPushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebPushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebPushSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JourneyItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorldId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    BankProblemId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneyItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JourneyItems_JourneyWorlds_WorldId",
                        column: x => x.WorldId,
                        principalTable: "JourneyWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LtiUserLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LtiPlatformId = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LtiUserLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LtiUserLinks_LtiPlatforms_LtiPlatformId",
                        column: x => x.LtiPlatformId,
                        principalTable: "LtiPlatforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LtiUserLinks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankProblemShares",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankProblemId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankProblemShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankProblemShares_BankProblems_BankProblemId",
                        column: x => x.BankProblemId,
                        principalTable: "BankProblems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankProblemShares_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankProblemId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Verdict = table.Column<int>(type: "integer", nullable: false),
                    RuntimeMs = table.Column<int>(type: "integer", nullable: false),
                    MemoryKb = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    CompilerOutput = table.Column<string>(type: "text", nullable: false),
                    FailedTest = table.Column<string>(type: "text", nullable: true),
                    XpAwarded = table.Column<int>(type: "integer", nullable: false),
                    LocalDay = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    JudgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CodePurgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankSubmissions_BankProblems_BankProblemId",
                        column: x => x.BankProblemId,
                        principalTable: "BankProblems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankSubmissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankTestCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankProblemId = table.Column<int>(type: "integer", nullable: false),
                    Stdin = table.Column<string>(type: "text", nullable: false),
                    ExpectedStdout = table.Column<string>(type: "text", nullable: false),
                    IsSample = table.Column<bool>(type: "boolean", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTestCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTestCases_BankProblems_BankProblemId",
                        column: x => x.BankProblemId,
                        principalTable: "BankProblems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BoardMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BoardId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    HiddenByTeacher = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardMemberships_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BoardMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LtiResourceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LtiPlatformId = table.Column<int>(type: "integer", nullable: false),
                    DeploymentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContextId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ResourceLinkId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    BoardId = table.Column<int>(type: "integer", nullable: true),
                    BankProblemId = table.Column<int>(type: "integer", nullable: true),
                    LineItemUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LtiResourceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LtiResourceLinks_BankProblems_BankProblemId",
                        column: x => x.BankProblemId,
                        principalTable: "BankProblems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LtiResourceLinks_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LtiResourceLinks_LtiPlatforms_LtiPlatformId",
                        column: x => x.LtiPlatformId,
                        principalTable: "LtiPlatforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProblemGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BoardId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Hidden = table.Column<bool>(type: "boolean", nullable: false),
                    ExamMode = table.Column<bool>(type: "boolean", nullable: false),
                    OpensAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosesAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenNotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CloseNotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProblemGroups_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProblemProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankProblemId = table.Column<int>(type: "integer", nullable: false),
                    BoardId = table.Column<int>(type: "integer", nullable: false),
                    ProposerId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProblemProposals_BankProblems_BankProblemId",
                        column: x => x.BankProblemId,
                        principalTable: "BankProblems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProblemProposals_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProblemProposals_Users_ProposerId",
                        column: x => x.ProposerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Problems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slug = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BoardId = table.Column<int>(type: "integer", nullable: false),
                    GroupId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StatementMarkdown = table.Column<string>(type: "text", nullable: false),
                    AllowedLanguages = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Tags = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    GeneratedByAi = table.Column<bool>(type: "boolean", nullable: false),
                    BannedHeaders = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BannedSymbols = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    InputFileName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TimeLimitMs = table.Column<int>(type: "integer", nullable: false),
                    MemoryLimitKb = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Hidden = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceBankProblemId = table.Column<int>(type: "integer", nullable: true),
                    BankProblemId = table.Column<int>(type: "integer", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Problems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Problems_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Problems_ProblemGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "ProblemGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Posts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BoardId = table.Column<int>(type: "integer", nullable: false),
                    ProblemId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    HiddenByStudent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivitySeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Posts_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Posts_Problems_ProblemId",
                        column: x => x.ProblemId,
                        principalTable: "Problems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Posts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Submissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProblemId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Verdict = table.Column<int>(type: "integer", nullable: false),
                    RuntimeMs = table.Column<int>(type: "integer", nullable: false),
                    MemoryKb = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    HiddenByStudent = table.Column<bool>(type: "boolean", nullable: false),
                    CompilerOutput = table.Column<string>(type: "text", nullable: false),
                    FailedTest = table.Column<string>(type: "text", nullable: true),
                    XpAwarded = table.Column<int>(type: "integer", nullable: false),
                    LocalDay = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    JudgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CodePurgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Submissions_Problems_ProblemId",
                        column: x => x.ProblemId,
                        principalTable: "Problems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Submissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProblemId = table.Column<int>(type: "integer", nullable: false),
                    Stdin = table.Column<string>(type: "text", nullable: false),
                    ExpectedStdout = table.Column<string>(type: "text", nullable: false),
                    IsSample = table.Column<bool>(type: "boolean", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestCases_Problems_ProblemId",
                        column: x => x.ProblemId,
                        principalTable: "Problems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostComments_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostComments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostReactions_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostReactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiHintEvents_At",
                table: "AiHintEvents",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AiHintProgresses_UserId_ProblemKey",
                table: "AiHintProgresses",
                columns: new[] { "UserId", "ProblemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderConfigs_OrganizationId",
                table: "AiProviderConfigs",
                column: "OrganizationId",
                unique: true,
                filter: "\"OrganizationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiReviewResults_Key",
                table: "AiReviewResults",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiSettings_OrganizationId",
                table: "AiSettings",
                column: "OrganizationId",
                unique: true,
                filter: "\"OrganizationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsages_UserId_Day",
                table: "AiUsages",
                columns: new[] { "UserId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_CreatedAt",
                table: "AuditLogEntries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BankProblems_IsPublic",
                table: "BankProblems",
                column: "IsPublic");

            migrationBuilder.CreateIndex(
                name: "IX_BankProblems_OwnerId",
                table: "BankProblems",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_BankProblems_Slug",
                table: "BankProblems",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankProblemShares_BankProblemId_UserId",
                table: "BankProblemShares",
                columns: new[] { "BankProblemId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankProblemShares_UserId",
                table: "BankProblemShares",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankSubmissions_BankProblemId_UserId",
                table: "BankSubmissions",
                columns: new[] { "BankProblemId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankSubmissions_UserId",
                table: "BankSubmissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTestCases_BankProblemId",
                table: "BankTestCases",
                column: "BankProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardMemberships_BoardId_UserId",
                table: "BoardMemberships",
                columns: new[] { "BoardId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardMemberships_UserId",
                table: "BoardMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Boards_JoinCode",
                table: "Boards",
                column: "JoinCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Boards_OrganizationId",
                table: "Boards",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Boards_OwnerId",
                table: "Boards",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Boards_Slug",
                table: "Boards",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailLoginCodes_UserId_CreatedAt",
                table: "EmailLoginCodes",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_TokenHash",
                table: "EmailVerificationTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_UserId_CreatedAt",
                table: "EmailVerificationTokens",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLogins_Provider_Subject",
                table: "ExternalLogins",
                columns: new[] { "Provider", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLogins_UserId",
                table: "ExternalLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_JourneyItems_WorldId",
                table: "JourneyItems",
                column: "WorldId");

            migrationBuilder.CreateIndex(
                name: "IX_JourneySkips_UserId_BankProblemId",
                table: "JourneySkips",
                columns: new[] { "UserId", "BankProblemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JourneyWorlds_JourneyId",
                table: "JourneyWorlds",
                column: "JourneyId");

            migrationBuilder.CreateIndex(
                name: "IX_LtiPlatforms_Issuer_ClientId",
                table: "LtiPlatforms",
                columns: new[] { "Issuer", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LtiPlatforms_OrganizationId",
                table: "LtiPlatforms",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_LtiResourceLinks_BankProblemId",
                table: "LtiResourceLinks",
                column: "BankProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_LtiResourceLinks_BoardId",
                table: "LtiResourceLinks",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_LtiResourceLinks_LtiPlatformId_DeploymentId_ContextId_Resou~",
                table: "LtiResourceLinks",
                columns: new[] { "LtiPlatformId", "DeploymentId", "ContextId", "ResourceLinkId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LtiSyncRetries_Kind_TargetId_UserId",
                table: "LtiSyncRetries",
                columns: new[] { "Kind", "TargetId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LtiSyncRetries_NextAttemptAt",
                table: "LtiSyncRetries",
                column: "NextAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_LtiUserLinks_LtiPlatformId_Subject",
                table: "LtiUserLinks",
                columns: new[] { "LtiPlatformId", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LtiUserLinks_UserId",
                table: "LtiUserLinks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MfaRecoveryCodes_UserId_CodeHash",
                table: "MfaRecoveryCodes",
                columns: new[] { "UserId", "CodeHash" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_PostId",
                table: "Notifications",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ReadAt",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvites_OrganizationId_UserId",
                table: "OrganizationInvites",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvites_UserId",
                table: "OrganizationInvites",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_OrganizationId_UserId",
                table: "OrganizationMemberships",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_UserId",
                table: "OrganizationMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Slug",
                table: "Organizations",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_TokenHash",
                table: "PasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId_CreatedAt",
                table: "PasswordResetTokens",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_PostId",
                table: "PostComments",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_UserId",
                table: "PostComments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PostReactions_PostId_UserId_Emoji",
                table: "PostReactions",
                columns: new[] { "PostId", "UserId", "Emoji" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostReactions_UserId",
                table: "PostReactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_BoardId",
                table: "Posts",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_ProblemId_UserId",
                table: "Posts",
                columns: new[] { "ProblemId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_UserId",
                table: "Posts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemGroups_BoardId",
                table: "ProblemGroups",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemLikes_BankProblemId",
                table: "ProblemLikes",
                column: "BankProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemLikes_ProblemId",
                table: "ProblemLikes",
                column: "ProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemLikes_UserId_BankProblemId",
                table: "ProblemLikes",
                columns: new[] { "UserId", "BankProblemId" },
                unique: true,
                filter: "\"BankProblemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemLikes_UserId_ProblemId",
                table: "ProblemLikes",
                columns: new[] { "UserId", "ProblemId" },
                unique: true,
                filter: "\"ProblemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemProposals_BankProblemId",
                table: "ProblemProposals",
                column: "BankProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemProposals_BoardId_Status",
                table: "ProblemProposals",
                columns: new[] { "BoardId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProblemProposals_ProposerId",
                table: "ProblemProposals",
                column: "ProposerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemReports_BankProblemId",
                table: "ProblemReports",
                column: "BankProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemReports_ProblemId",
                table: "ProblemReports",
                column: "ProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemReports_Status_CreatedAt",
                table: "ProblemReports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProblemReports_UserId",
                table: "ProblemReports",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Problems_BoardId",
                table: "Problems",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_Problems_GroupId",
                table: "Problems",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Problems_Slug",
                table: "Problems",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SolveRecords_UserId_ProblemKey",
                table: "SolveRecords",
                columns: new[] { "UserId", "ProblemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatementImages_OwnerId",
                table: "StatementImages",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ProblemId_UserId",
                table: "Submissions",
                columns: new[] { "ProblemId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_UserId",
                table: "Submissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_UserId",
                table: "SurveyResponses",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_ProblemId",
                table: "TestCases",
                column: "ProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBadges_UserId_Key",
                table: "UserBadges",
                columns: new[] { "UserId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPasskeys_CredentialId",
                table: "UserPasskeys",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPasskeys_UserId",
                table: "UserPasskeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebPushSubscriptions_Endpoint",
                table: "WebPushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebPushSubscriptions_UserId",
                table: "WebPushSubscriptions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiHintEvents");

            migrationBuilder.DropTable(
                name: "AiHintProgresses");

            migrationBuilder.DropTable(
                name: "AiProviderConfigs");

            migrationBuilder.DropTable(
                name: "AiReviewResults");

            migrationBuilder.DropTable(
                name: "AiSettings");

            migrationBuilder.DropTable(
                name: "AiUsages");

            migrationBuilder.DropTable(
                name: "AiUserSettings");

            migrationBuilder.DropTable(
                name: "AuditLogEntries");

            migrationBuilder.DropTable(
                name: "BankProblemEmbeddings");

            migrationBuilder.DropTable(
                name: "BankProblemShares");

            migrationBuilder.DropTable(
                name: "BankSubmissions");

            migrationBuilder.DropTable(
                name: "BankTestCases");

            migrationBuilder.DropTable(
                name: "BoardMemberships");

            migrationBuilder.DropTable(
                name: "EmailLoginCodes");

            migrationBuilder.DropTable(
                name: "EmailVerificationTokens");

            migrationBuilder.DropTable(
                name: "ExternalLogins");

            migrationBuilder.DropTable(
                name: "JourneyItems");

            migrationBuilder.DropTable(
                name: "JourneySkips");

            migrationBuilder.DropTable(
                name: "LtiResourceLinks");

            migrationBuilder.DropTable(
                name: "LtiSyncRetries");

            migrationBuilder.DropTable(
                name: "LtiToolKeys");

            migrationBuilder.DropTable(
                name: "LtiUserLinks");

            migrationBuilder.DropTable(
                name: "MfaRecoveryCodes");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OrganizationInvites");

            migrationBuilder.DropTable(
                name: "OrganizationMemberships");

            migrationBuilder.DropTable(
                name: "PasswordResetTokens");

            migrationBuilder.DropTable(
                name: "PlatformRuntimeSettings");

            migrationBuilder.DropTable(
                name: "PostComments");

            migrationBuilder.DropTable(
                name: "PostReactions");

            migrationBuilder.DropTable(
                name: "ProblemLikes");

            migrationBuilder.DropTable(
                name: "ProblemProposals");

            migrationBuilder.DropTable(
                name: "ProblemReports");

            migrationBuilder.DropTable(
                name: "SolveRecords");

            migrationBuilder.DropTable(
                name: "StatementImages");

            migrationBuilder.DropTable(
                name: "Submissions");

            migrationBuilder.DropTable(
                name: "SurveyResponses");

            migrationBuilder.DropTable(
                name: "TestCases");

            migrationBuilder.DropTable(
                name: "UserBadges");

            migrationBuilder.DropTable(
                name: "UserPasskeys");

            migrationBuilder.DropTable(
                name: "WebPushSubscriptions");

            migrationBuilder.DropTable(
                name: "JourneyWorlds");

            migrationBuilder.DropTable(
                name: "LtiPlatforms");

            migrationBuilder.DropTable(
                name: "Posts");

            migrationBuilder.DropTable(
                name: "BankProblems");

            migrationBuilder.DropTable(
                name: "Journeys");

            migrationBuilder.DropTable(
                name: "Problems");

            migrationBuilder.DropTable(
                name: "ProblemGroups");

            migrationBuilder.DropTable(
                name: "Boards");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
