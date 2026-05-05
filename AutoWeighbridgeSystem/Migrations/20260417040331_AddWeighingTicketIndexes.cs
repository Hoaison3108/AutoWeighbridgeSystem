using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoWeighbridgeSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddWeighingTicketIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WeighingTickets_IsVoid",
                table: "WeighingTickets");

            migrationBuilder.DropIndex(
                name: "IX_WeighingTickets_LicensePlate",
                table: "WeighingTickets");

            migrationBuilder.UpdateData(
                table: "WeighingTickets",
                keyColumn: "TicketID",
                keyValue: "260412-001",
                columns: new[] { "TimeIn", "TimeOut" },
                values: new object[] { new DateTime(2026, 4, 16, 11, 3, 29, 777, DateTimeKind.Local).AddTicks(5195), new DateTime(2026, 4, 16, 11, 18, 29, 777, DateTimeKind.Local).AddTicks(5208) });

            migrationBuilder.CreateIndex(
                name: "IX_WeighingTickets_OpenTicketLookup",
                table: "WeighingTickets",
                columns: new[] { "LicensePlate", "IsVoid", "TimeOut" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WeighingTickets_OpenTicketLookup",
                table: "WeighingTickets");

            migrationBuilder.UpdateData(
                table: "WeighingTickets",
                keyColumn: "TicketID",
                keyValue: "260412-001",
                columns: new[] { "TimeIn", "TimeOut" },
                values: new object[] { new DateTime(2026, 4, 16, 7, 46, 12, 297, DateTimeKind.Local).AddTicks(9934), new DateTime(2026, 4, 16, 8, 1, 12, 297, DateTimeKind.Local).AddTicks(9946) });

            migrationBuilder.CreateIndex(
                name: "IX_WeighingTickets_IsVoid",
                table: "WeighingTickets",
                column: "IsVoid");

            migrationBuilder.CreateIndex(
                name: "IX_WeighingTickets_LicensePlate",
                table: "WeighingTickets",
                column: "LicensePlate");
        }
    }
}
