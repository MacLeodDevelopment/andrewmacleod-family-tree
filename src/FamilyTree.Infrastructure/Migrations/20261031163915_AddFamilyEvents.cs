using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FamilyEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Date = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Place = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyEvents_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyEvents_FamilyId",
                table: "FamilyEvents",
                column: "FamilyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FamilyEvents");
        }
    }
}
