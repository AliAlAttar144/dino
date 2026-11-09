using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toetsr.Data.Migrations
{
    /// <inheritdoc />
    public partial class QuestionExcluded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Excluded",
                table: "Questions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Excluded",
                table: "Questions");
        }
    }
}
