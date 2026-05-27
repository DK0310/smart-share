using System.Security.Claims;

namespace FileShare.API.Extensions;

/// <summary>
/// Extension method for extracting user ID from JWT claims.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
