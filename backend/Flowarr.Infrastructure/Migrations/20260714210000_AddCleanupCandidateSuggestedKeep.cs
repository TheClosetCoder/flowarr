using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Flowarr.Infrastructure.Data;

#nullable disable

namespace Flowarr.Infrastructure.Migrations;

[DbContext(typeof(FlowarrDbContext))]
[Migration("20260714210000_AddCleanupCandidateSuggestedKeep")]
public partial class AddCleanupCandidateSuggestedKeep : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "CleanupCandidates" ADD COLUMN IF NOT EXISTS "IsSuggestedKeep" boolean NOT NULL DEFAULT FALSE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
