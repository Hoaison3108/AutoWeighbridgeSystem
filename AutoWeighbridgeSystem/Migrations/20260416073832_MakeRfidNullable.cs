using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoWeighbridgeSystem.Migrations
{
    /// <inheritdoc />
    public partial class MakeRfidNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehicles_RfidCardId",
                table: "Vehicles");

            migrationBuilder.AlterColumn<string>(
                name: "RfidCardId",
                table: "Vehicles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.UpdateData(
                table: "WeighingTickets",
                keyColumn: "TicketID",
                keyValue: "260412-001",
                columns: new[] { "TimeIn", "TimeOut" },
                values: new object[] { new DateTime(2026, 4, 15, 14, 38, 32, 196, DateTimeKind.Local).AddTicks(8425), new DateTime(2026, 4, 15, 14, 53, 32, 196, DateTimeKind.Local).AddTicks(8437) });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_RfidCardId",
                table: "Vehicles",
                column: "RfidCardId",
                unique: true,
                filter: "[IsDeleted] = 0 AND [RfidCardId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehicles_RfidCardId",
                table: "Vehicles");

            migrationBuilder.AlterColumn<string>(
                name: "RfidCardId",
                table: "Vehicles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "WeighingTickets",
                keyColumn: "TicketID",
                keyValue: "260412-001",
                columns: new[] { "TimeIn", "TimeOut" },
                values: new object[] { new DateTime(2026, 4, 15, 14, 27, 47, 936, DateTimeKind.Local).AddTicks(5107), new DateTime(2026, 4, 15, 14, 42, 47, 936, DateTimeKind.Local).AddTicks(5124) });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_RfidCardId",
                table: "Vehicles",
                column: "RfidCardId",
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
