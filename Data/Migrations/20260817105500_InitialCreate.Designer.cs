using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using oyinQ.Bot.Data;
using oyinQ.Bot.Data.Entities;

#nullable disable

namespace oyinQ.Bot.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260817105500_InitialCreate")]
partial class InitialCreate
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        AppDbContextModelSnapshot.BuildModelStatic(modelBuilder);
    }
}
