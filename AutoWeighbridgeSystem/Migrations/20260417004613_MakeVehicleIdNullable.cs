using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoWeighbridgeSystem.Migrations
{
    /// <inheritdoc />
    public partial class MakeVehicleIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "VehicleId",
                table: "WeighingTickets",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.UpdateData(
                table: "WeighingTickets",
                keyColumn: "TicketID",
                keyValue: "260412-001",
                columns: new[] { "TimeIn", "TimeOut" },
                values: new object[] { new DateTime(2026, 4, 16, 7, 46, 12, 297, DateTimeKind.Local).AddTicks(9934), new DateTime(2026, 4, 16, 8, 1, 12, 297, DateTimeKind.Local).AddTicks(9946) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "VehicleId",
                table: "WeighingTickets",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "WeighingTickets",
                keyColumn: "TicketID",
                keyValue: "260412-001",
                columns: new[] { "TimeIn", "TimeOut" },
                values: new object[] { new DateTime(2026, 4, 15, 14, 38, 32, 196, DateTimeKind.Local).AddTicks(8425), new DateTime(2026, 4, 15, 14, 53, 32, 196, DateTimeKind.Local).AddTicks(8437) });
        }
    }
}
