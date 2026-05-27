using System.Text;
using FileShare.API.Middleware;
using FileShare.Application.Interfaces;
using FileShare.Application.Services;
using FileShare.Infrastructure.Persistence;
using FileShare.Infrastructure.Repositories;
using FileShare.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ──────────────────────────────────────────────────
// Database
// ──────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// ──────────────────────────────────────────────────
// Services
// ──────────────────────────────────────────────────
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddHostedService<CleanupService>();

// ──────────────────────────────────────────────────
// Repositories
// ──────────────────────────────────────────────────
builder.Services.AddScoped<IFileRepository, FileRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

// ──────────────────────────────────────────────────
// Storage (swap implementation based on config)
// ──────────────────────────────────────────────────
var storageProvider = builder.Configuration["Storage:Provider"];
// Future: if (storageProvider == "azure") register BlobStorageProvider
builder.Services.AddScoped<IStorageProvider, LocalStorageProvider>();

// ──────────────────────────────────────────────────
// JWT Authentication
// ──────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!)),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

builder.Services.AddAuthorization();

// ──────────────────────────────────────────────────
// CORS
// ──────────────────────────────────────────────────
builder.Services.AddCors(opt => opt.AddPolicy("Frontend", p =>
    p.WithOrigins(builder.Configuration["Frontend:Url"] ?? "http://localhost:5173")
     .AllowAnyMethod()
     .AllowAnyHeader()));

// ──────────────────────────────────────────────────
// Controllers
// ──────────────────────────────────────────────────
builder.Services.AddControllers();

var app = builder.Build();

// ──────────────────────────────────────────────────
// Middleware pipeline — ORDER MATTERS
// ──────────────────────────────────────────────────
app.UseMiddleware<ErrorHandlingMiddleware>();  // 1. Catch all unhandled exceptions
app.UseCors("Frontend");                      // 2. CORS headers
app.UseAuthentication();                      // 3. Validate JWT
app.UseAuthorization();                       // 4. Enforce [Authorize]
app.UseStaticFiles();                         // 5. Serve wwwroot
app.MapControllers();                         // 6. Route to controllers

// ──────────────────────────────────────────────────
// Auto-migrate database on startup (dev convenience)
// ──────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
