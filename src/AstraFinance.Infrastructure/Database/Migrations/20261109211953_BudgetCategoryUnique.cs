using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AstraFinance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class BudgetCategoryUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets",
                column: "CategoryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets",
                column: "CategoryId");
        }
    }
}
