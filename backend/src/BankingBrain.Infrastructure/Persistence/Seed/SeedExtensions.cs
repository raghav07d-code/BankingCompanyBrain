using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BankingBrain.Infrastructure.Persistence.Seed;

public static class SeedExtensions
{
    public static async Task SeedDatabaseAsync(
        this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var seeder =
            scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

        await seeder.SeedAsync();
    }
}