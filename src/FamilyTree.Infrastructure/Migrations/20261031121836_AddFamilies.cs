using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Families",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Partner1Id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Partner2Id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Families", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Families_People_Partner1Id",
                        column: x => x.Partner1Id,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Families_People_Partner2Id",
                        column: x => x.Partner2Id,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FamilyChildren",
                columns: table => new
                {
                    ChildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Partner1Relationship = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Partner2Relationship = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyChildren", x => new { x.FamilyId, x.ChildId });
                    table.ForeignKey(
                        name: "FK_FamilyChildren_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FamilyChildren_People_ChildId",
                        column: x => x.ChildId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Families_Partner1Id",
                table: "Families",
                column: "Partner1Id");

            migrationBuilder.CreateIndex(
                name: "IX_Families_Partner2Id",
                table: "Families",
                column: "Partner2Id");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyChildren_ChildId",
                table: "FamilyChildren",
                column: "ChildId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FamilyChildren");

            migrationBuilder.DropTable(
                name: "Families");
        }
    }
}
