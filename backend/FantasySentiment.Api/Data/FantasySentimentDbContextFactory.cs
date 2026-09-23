using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FantasySentiment.Api.Data;

public sealed class FantasySentimentDbContextFactory : IDesignTimeDbContextFactory<FantasySentimentDbContext>
{
    public FantasySentimentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FantasySentimentDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("DATABASE_URL")
                      ?? "Host=localhost;Port=55432;Database=fantasy_sentiment;Username=fantasy;Password=replace-with-local-password")
            .Options;

        return new FantasySentimentDbContext(options);
    }
}
