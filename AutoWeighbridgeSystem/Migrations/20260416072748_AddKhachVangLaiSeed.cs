using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoWeighbridgeSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddKhachVangLaiSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Customers",
                columns: new[] { "CustomerId", "CustomerName", "IsDeleted" },
                values: new object[] { "KVL", "Khách vãng lai", false });

            migrationBuilder.UpdateData(
                table: "WeighingTickets",
                keyColumn: "TicketID",
                keyValue: "260412-001",
                columns: new[] { "TimeIn", "TimeOut" },
                values: new object[] { new DateTime(2026, 4, 15, 14, 27, 47, 936, DateTimeKind.Local).AddTicks(5107), new DateTime(2026, 4, 15, 14, 42, 47, 936, DateTimeKind.Local).AddTicks(5124) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: "KVL");

            migrationBuilder.UpdateData(
                table: "WeighingTickets",
                keyColumn: "TicketID",
                keyValue: "260412-001",
                columns: new[] { "TimeIn", "TimeOut" },
                values: new object[] { new DateTime(2026, 4, 11, 18, 13, 50, 575, DateTimeKind.Local).AddTicks(5770), new DateTime(2026, 4, 11, 18, 28, 50, 575, DateTimeKind.Local).AddTicks(5783) });
        }
    }
}
