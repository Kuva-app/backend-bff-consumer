using Kuva.ConsumerBff.Repository.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kuva.ConsumerBff.EFMigrations;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ConsumerBffDbContext>
{
    public ConsumerBffDbContext CreateDbContext(string[] args)
    {
        const string connectionString =
            "Server=localhost,1433;Database=KuvaConsumerBff;User Id=sa;Password=Change_this_password_123!;Encrypt=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<ConsumerBffDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.FullName);
                sql.EnableRetryOnFailure(5);
            })
            .Options;

        return new ConsumerBffDbContext(options);
    }
}
