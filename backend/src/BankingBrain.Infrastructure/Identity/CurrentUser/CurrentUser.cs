using System.Security.Claims;
using BankingBrain.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace BankingBrain.Infrastructure.Identity.CurrentUser;

public sealed class CurrentUser : ICurrentUser
{
    public Guid UserId { get; }

    public string Email { get; } = string.Empty;

    public string FullName { get; } = string.Empty;

    public bool IsAuthenticated { get; }

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;

        IsAuthenticated = user?.Identity?.IsAuthenticated ?? false;

        if (!IsAuthenticated)
            return;

        var principal = user!;
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");

        if (!Guid.TryParse(subject, out var userId))
        {
            IsAuthenticated = false;
            return;
        }

        UserId = userId;

        Email =
            principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email")
            ?? string.Empty;

        FullName =
            principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? string.Empty;
    }
}