using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Flowarr.Infrastructure.Data;

#nullable disable

namespace Flowarr.Infrastructure.Migrations;

[DbContext(typeof(FlowarrDbContext))]
[Migration("20260714120000_AddStorageLocationCategory")]
public partial class AddStorageLocationCategory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "StorageLocations" ADD COLUMN IF NOT EXISTS "Category" integer NOT NULL DEFAULT 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
