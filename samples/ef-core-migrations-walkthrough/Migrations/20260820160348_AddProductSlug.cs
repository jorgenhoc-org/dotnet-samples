using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JorgenHoc.EfCoreMigrationsWalkthrough.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HAND-EDITED. EF generated a single non-nullable AddColumn with defaultValue "".
            // That compiles but leaves every existing row with an empty slug. The article's
            // customization pattern is safer: add the column nullable, backfill it from real
            // data, THEN tighten it to non-nullable. The three steps below replace the one
            // EF wrote. See https://www.jorgenhoc.org/en/blog/ef-core-migrations-walkthrough
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);

            // Backfill from existing Name data before the column becomes required.
            migrationBuilder.Sql(
                "UPDATE Products SET Slug = LOWER(REPLACE(Name, ' ', '-')) WHERE Slug IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Products",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Products");
        }
    }
}
