using BankingBrain.Domain.Entities;
using BankingBrain.Infrastructure.Identity.Passwords;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BankingBrain.Infrastructure.Persistence.Seed;

public sealed class DatabaseSeeder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public DatabaseSeeder(
        ApplicationDbContext dbContext,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    public async Task SeedAsync()
    {
        await _dbContext.Database.MigrateAsync();

        var adminEmail = _configuration["Seed:AdminEmail"];
        var adminPassword = _configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(adminEmail) ||
            string.IsNullOrWhiteSpace(adminPassword))
            return;

        var adminExists = await _dbContext.Users.AnyAsync(
            x => x.Email == adminEmail.Trim().ToLowerInvariant());

        if (adminExists)
            return;

        var hasher = new PasswordHasher();

        var admin = User.Create(
            adminEmail,
            hasher.Hash(adminPassword),
            "System Administrator");

        _dbContext.Users.Add(admin);

        await _dbContext.SaveChangesAsync();
    }
}