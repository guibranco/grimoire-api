using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Grimoire.Infrastructure.Persistence;

public class GrimoireDbContextFactory : IDesignTimeDbContextFactory<GrimoireDbContext>
{
    public GrimoireDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../Grimoire.Api"))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<GrimoireDbContext>();
        optionsBuilder.UseSqlite(config.GetConnectionString("Default"));
        return new GrimoireDbContext(optionsBuilder.Options);
    }
}
