using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Flowarr.Infrastructure.Data;

#nullable disable

namespace Flowarr.Infrastructure.Migrations;

[DbContext(typeof(FlowarrDbContext))]
[Migration("20260714150000_AddCleanupCandidatePlexFields")]
public partial class AddCleanupCandidatePlexFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "CleanupCandidates" ADD COLUMN IF NOT EXISTS "ViewCount" integer NOT NULL DEFAULT 0;
            ALTER TABLE "CleanupCandidates" ADD COLUMN IF NOT EXISTS "LastViewedAt" timestamp with time zone NULL;
            ALTER TABLE "CleanupCandidates" ADD COLUMN IF NOT EXISTS "AddedAt" timestamp with time zone NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
