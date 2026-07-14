using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Flowarr.Infrastructure.Data;

#nullable disable

namespace Flowarr.Infrastructure.Migrations;

[DbContext(typeof(FlowarrDbContext))]
[Migration("20260714200000_AddCleanupCandidateGroupingFields")]
public partial class AddCleanupCandidateGroupingFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "CleanupCandidates" ADD COLUMN IF NOT EXISTS "SeriesTitle" text NULL;
            ALTER TABLE "CleanupCandidates" ADD COLUMN IF NOT EXISTS "SeasonNumber" integer NULL;
            ALTER TABLE "CleanupCandidates" ADD COLUMN IF NOT EXISTS "EpisodeNumber" integer NULL;
            ALTER TABLE "CleanupCandidates" ADD COLUMN IF NOT EXISTS "MediaType" text NULL;
            ALTER TABLE "CleanupCandidates" ADD COLUMN IF NOT EXISTS "ReasonType" integer NOT NULL DEFAULT 1;
            ALTER TABLE "CleanupCandidates" ADD COLUMN IF NOT EXISTS "DuplicateGroupKey" text NULL;
            ALTER TABLE "CleanupCandidates" ADD COLUMN IF NOT EXISTS "KeptPath" text NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
