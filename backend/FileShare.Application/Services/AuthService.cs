using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FileShare.Application.DTOs;
using FileShare.Application.Interfaces;
using FileShare.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace FileShare.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepo;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IUserRepository userRepo, IConfiguration config, ILogger<AuthService> logger)
    {
        _userRepo = userRepo;
        _config = config;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Result<AuthResponse>.Failure("Email is required.");

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            return Result<AuthResponse>.Failure("Password must be at least 6 characters.");

        var existing = await _userRepo.GetByEmailAsync(request.Email.ToLowerInvariant());
        if (existing is not null)
            return Result<AuthResponse>.Failure("An account with this email already exists.");

        var user = new User
        {
            Email = request.Email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        await _userRepo.AddAsync(user);

        var token = GenerateJwt(user);
        _logger.LogInformation("User registered: {Email}", user.Email);

        return Result<AuthResponse>.Success(new AuthResponse
        {
            Token = token,
            Email = user.Email
        });
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Result<AuthResponse>.Failure("Email and password are required.");

        var user = await _userRepo.GetByEmailAsync(request.Email.ToLowerInvariant());
        if (user is null)
            return Result<AuthResponse>.Failure("Invalid email or password.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Result<AuthResponse>.Failure("Invalid email or password.");

        var token = GenerateJwt(user);
        _logger.LogInformation("User logged in: {Email}", user.Email);

        return Result<AuthResponse>.Success(new AuthResponse
        {
            Token = token,
            Email = user.Email
        });
    }

    private string GenerateJwt(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email)
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
