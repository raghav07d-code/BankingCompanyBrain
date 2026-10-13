using BankingBrain.Application.Features.Authentication.Interfaces;
using BankingBrain.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace BankingBrain.Infrastructure.Identity.Passwords;

public sealed class PasswordHasher : IPasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.IPasswordHasher<User> _passwordHasher;

    public PasswordHasher()
    {
        _passwordHasher =
            new Microsoft.AspNetCore.Identity.PasswordHasher<User>();
    }

    public string Hash(string password)
    {
        return _passwordHasher.HashPassword(
            User.Create(
            "placeholder@brain.local",
            string.Empty,
            "Placeholder"),
            password);
    }

    public bool Verify(
        string password,
        string passwordHash)
    {
        var result = _passwordHasher.VerifyHashedPassword(
           User.Create(
        "placeholder@brain.local",
        string.Empty,
        "Placeholder"),
                passwordHash,
            password);

        return result != PasswordVerificationResult.Failed;
    }
}