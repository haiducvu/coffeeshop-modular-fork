using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeShop.Modules.Counter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAtUtc",
                schema: "counter",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                schema: "counter",
                table: "outbox_messages");
        }
    }
}
