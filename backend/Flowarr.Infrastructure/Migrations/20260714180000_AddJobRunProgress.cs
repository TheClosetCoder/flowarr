using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Flowarr.Infrastructure.Data;

#nullable disable

namespace Flowarr.Infrastructure.Migrations;

[DbContext(typeof(FlowarrDbContext))]
[Migration("20260714180000_AddJobRunProgress")]
public partial class AddJobRunProgress : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "JobRuns" ADD COLUMN IF NOT EXISTS "Progress" text NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
