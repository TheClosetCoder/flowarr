using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Flowarr.Infrastructure.Data;

#nullable disable

namespace Flowarr.Infrastructure.Migrations;

[DbContext(typeof(FlowarrDbContext))]
[Migration("20260507000100_ProductionBaseline")]
public partial class ProductionBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "AdminUsers" (
                "Id" uuid NOT NULL,
                "Username" text NOT NULL,
                "PasswordHash" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_AdminUsers" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "AuditLogs" (
                "Id" uuid NOT NULL,
                "Action" text NOT NULL,
                "TargetType" text NOT NULL,
                "TargetId" uuid NULL,
                "Path" text NULL,
                "Actor" text NOT NULL,
                "DryRun" boolean NOT NULL,
                "Details" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "Downloads" (
                "Id" uuid NOT NULL,
                "Client" text NOT NULL,
                "Name" text NOT NULL,
                "Hash" text NULL,
                "Status" integer NOT NULL,
                "Progress" numeric NOT NULL,
                "SizeBytes" bigint NOT NULL,
                "RemotePath" text NULL,
                "MediaItemId" uuid NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_Downloads" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "IntegrationConnections" (
                "Id" uuid NOT NULL,
                "Key" text NOT NULL,
                "DisplayName" text NOT NULL,
                "Enabled" boolean NOT NULL,
                "SettingsJson" text NOT NULL,
                "SecretsJson" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_IntegrationConnections" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "JobRuns" (
                "Id" uuid NOT NULL,
                "JobType" text NOT NULL,
                "Status" integer NOT NULL,
                "StartedAt" timestamp with time zone NOT NULL,
                "CompletedAt" timestamp with time zone NULL,
                "ErrorMessage" text NULL,
                CONSTRAINT "PK_JobRuns" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "MediaItems" (
                "Id" uuid NOT NULL,
                "Title" text NOT NULL,
                "ExternalId" text NULL,
                "SourceSystem" text NOT NULL,
                "ExistsInLibrary" boolean NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_MediaItems" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "MediaFiles" (
                "Id" uuid NOT NULL,
                "MediaItemId" uuid NOT NULL,
                "Path" text NOT NULL,
                "RemotePath" text NULL,
                "SizeBytes" bigint NOT NULL,
                "ExistsLocally" boolean NOT NULL,
                "ExistsOnSeedbox" boolean NOT NULL,
                "ImportedLocally" boolean NOT NULL,
                "LastSeenAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_MediaFiles" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "CleanupCandidates" (
                "Id" uuid NOT NULL,
                "MediaFileId" uuid NULL,
                "DownloadId" uuid NULL,
                "Path" text NOT NULL,
                "SizeBytes" bigint NOT NULL,
                "Reason" text NOT NULL,
                "Confidence" integer NOT NULL,
                "Status" integer NOT NULL,
                "DryRun" boolean NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "ReviewedAt" timestamp with time zone NULL,
                "ReviewedBy" text NULL,
                CONSTRAINT "PK_CleanupCandidates" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "RemoteTransfers" (
                "Id" uuid NOT NULL,
                "DownloadId" uuid NULL,
                "Provider" text NOT NULL,
                "RemotePath" text NOT NULL,
                "LocalPath" text NOT NULL,
                "BytesTransferred" bigint NOT NULL,
                "Status" integer NOT NULL,
                "Message" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "StartedAt" timestamp with time zone NULL,
                "CompletedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_RemoteTransfers" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS "StorageLocations" (
                "Id" uuid NOT NULL,
                "Name" text NOT NULL,
                "Kind" integer NOT NULL,
                "PathOrEndpoint" text NOT NULL,
                "UsedBytes" bigint NOT NULL,
                "FreeBytes" bigint NOT NULL,
                "LastScannedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_StorageLocations" PRIMARY KEY ("Id")
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AdminUsers_Username" ON "AdminUsers" ("Username");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_IntegrationConnections_Key" ON "IntegrationConnections" ("Key");
            CREATE INDEX IF NOT EXISTS "IX_JobRuns_JobType_StartedAt" ON "JobRuns" ("JobType", "StartedAt");
            CREATE INDEX IF NOT EXISTS "IX_MediaFiles_MediaItemId" ON "MediaFiles" ("MediaItemId");
            CREATE INDEX IF NOT EXISTS "IX_CleanupCandidates_DownloadId" ON "CleanupCandidates" ("DownloadId");
            CREATE INDEX IF NOT EXISTS "IX_CleanupCandidates_MediaFileId" ON "CleanupCandidates" ("MediaFileId");

            DELETE FROM "Downloads"
            WHERE "Client" = 'qBittorrent'
              AND (("Name" = 'Example.Show.S01E01' AND "RemotePath" = '/seedbox/incomplete/Example.Show.S01E01')
                   OR ("Name" = 'Example.Movie.2025.1080p' AND "RemotePath" = '/seedbox/completed/Example.Movie.2025.1080p'));
            DELETE FROM "CleanupCandidates" WHERE "Path" LIKE '%Example Movie%' OR "Path" LIKE '%Example.Movie%';
            DELETE FROM "MediaFiles" WHERE "Path" LIKE '%Example Movie%' OR "Path" LIKE '%Example.Movie%' OR "RemotePath" LIKE '%Example Movie%' OR "RemotePath" LIKE '%Example.Movie%';
            DELETE FROM "MediaItems" WHERE "Title" = 'Example Movie' OR "ExternalId" = 'radarr-1';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
