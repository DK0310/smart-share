---
name: fileshare-testing
description: Use when writing unit tests, integration tests, or verifying backend logic in the File & Image Sharing Service. Covers xUnit, Moq, WebApplicationFactory, and test database setup.
---

# Smart Share — Testing Skill

## Overview

Smart Share uses **xUnit** as the test framework, **Moq** for mocking dependencies, and **WebApplicationFactory** for integration tests. Unit tests verify individual service methods in isolation. Integration tests verify the full HTTP pipeline — controller → service → repository → database.

**Rule:** Every service method must have at least one success-path test and one failure-path test. Integration tests must cover the core upload → retrieve → delete pipeline end-to-end.

---

## Test Project Structure

```
backend/
├── FileShare.API/
├── FileShare.Application/
├── FileShare.Domain/
├── FileShare.Infrastructure/
│
├── FileShare.Tests/                        ← Unit tests
│   ├── Services/
│   │   ├── FileServiceTests.cs            ← Core business logic tests
│   │   └── AuthServiceTests.cs            ← Auth logic tests
│   └── FileShare.Tests.csproj
│
└── FileShare.IntegrationTests/             ← Integration tests
    ├── FilesEndpointTests.cs              ← Full HTTP pipeline tests
    ├── AuthEndpointTests.cs               ← Auth flow tests
    ├── CustomWebApplicationFactory.cs     ← Test server setup
    └── FileShare.IntegrationTests.csproj
```

---

## NuGet Packages

### Unit Test Project (`FileShare.Tests`)

```bash
dotnet add package xunit
dotnet add package xunit.runner.visualstudio
dotnet add package Moq
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package FluentAssertions          # Optional but recommended
```

### Integration Test Project (`FileShare.IntegrationTests`)

```bash
dotnet add package xunit
dotnet add package xunit.runner.visualstudio
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package Microsoft.EntityFrameworkCore.InMemory
dotnet add package Microsoft.NET.Test.Sdk
```

---

## Unit Tests (xUnit + Moq)

### Purpose

Unit tests verify **service-layer business logic** in isolation. All dependencies (repository, storage, config) are mocked. No database, no disk, no HTTP.

### When to Write Unit Tests

```
WRITE A UNIT TEST WHEN:
├─ Adding a new service method
├─ Modifying validation logic
├─ Changing business rules (size limits, MIME checks, expiry)
├─ Adding a new Result<T> failure path
└─ Fixing a bug (write test that reproduces the bug first)
```

### Test Naming Convention

```
{MethodName}_{ExpectedResult}_{Condition}

Examples:
  Upload_ReturnsSuccess_WhenFileIsValid
  Upload_ReturnsFailure_WhenFileTooLarge
  Upload_ReturnsFailure_WhenFileEmpty
  Upload_ReturnsFailure_WhenMimeTypeNotAllowed
  Delete_ReturnsFailure_WhenNotOwner
  Delete_ReturnsFailure_WhenFileNotFound
  GetFile_ReturnsFailure_WhenExpired
  GetFile_IncrementsDownloadCount_WhenSuccessful
```

### Complete FileService Unit Tests

```csharp
using Moq;
using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using FileShare.Application.Services;
using FileShare.Application.Interfaces;
using FileShare.Application.DTOs;
using FileShare.Domain.Entities;

public class FileServiceTests
{
    // ── Arrange: Create mocks and System Under Test ─────────
    private readonly Mock<IFileRepository> _repoMock = new();
    private readonly Mock<IStorageProvider> _storageMock = new();
    private readonly IConfiguration _config;
    private readonly FileService _sut;

    public FileServiceTests()
    {
        _config = new ConfigurationBuilder().Build();
        _sut = new FileService(_repoMock.Object, _storageMock.Object, _config);
    }

    // ── Success Path ────────────────────────────────────────

    [Fact]
    public async Task Upload_ReturnsSuccess_WhenFileIsValid()
    {
        // Arrange
        var file = CreateMockFile(size: 1024, contentType: "image/jpeg");
        var request = new UploadFileRequest { File = file };

        _repoMock.Setup(r => r.GetByCodeAsync(It.IsAny<string>()))
            .ReturnsAsync((FileRecord?)null); // No collision
        _storageMock.Setup(s => s.SaveAsync(It.IsAny<IFormFile>(), It.IsAny<string>()))
            .ReturnsAsync("test.jpg");

        // Act
        var result = await _sut.UploadAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("image/jpeg", result.Value.MimeType);
        Assert.Equal(1024, result.Value.SizeBytes);

        // Verify side effects
        _storageMock.Verify(s => s.SaveAsync(It.IsAny<IFormFile>(), It.IsAny<string>()), Times.Once);
        _repoMock.Verify(r => r.AddAsync(It.IsAny<FileRecord>()), Times.Once);
    }

    // ── Validation Failures ─────────────────────────────────

    [Fact]
    public async Task Upload_ReturnsFailure_WhenFileEmpty()
    {
        var file = CreateMockFile(size: 0);
        var request = new UploadFileRequest { File = file };

        var result = await _sut.UploadAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
        // Verify NO side effects occurred
        _storageMock.Verify(s => s.SaveAsync(It.IsAny<IFormFile>(), It.IsAny<string>()), Times.Never);
        _repoMock.Verify(r => r.AddAsync(It.IsAny<FileRecord>()), Times.Never);
    }

    [Fact]
    public async Task Upload_ReturnsFailure_WhenFileTooLarge()
    {
        var file = CreateMockFile(size: 11 * 1024 * 1024);
        var request = new UploadFileRequest { File = file };

        var result = await _sut.UploadAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Contains("10 MB", result.Error);
    }

    [Fact]
    public async Task Upload_ReturnsFailure_WhenMimeTypeNotAllowed()
    {
        var file = CreateMockFile(size: 1024, contentType: "application/x-executable");
        var request = new UploadFileRequest { File = file };

        var result = await _sut.UploadAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Contains("not allowed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Get File ────────────────────────────────────────────

    [Fact]
    public async Task GetFile_ReturnsSuccess_WhenFileExists()
    {
        var file = new FileRecord
        {
            Code = "abc12",
            OriginalFilename = "test.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 1024,
            StoragePath = "abc12.jpg"
        };
        _repoMock.Setup(r => r.GetByCodeAsync("abc12")).ReturnsAsync(file);

        var result = await _sut.GetFileAsync("abc12");

        Assert.True(result.IsSuccess);
        Assert.Equal("abc12", result.Value.Code);
        _repoMock.Verify(r => r.IncrementDownloadCountAsync("abc12"), Times.Once);
    }

    [Fact]
    public async Task GetFile_ReturnsFailure_WhenFileNotFound()
    {
        _repoMock.Setup(r => r.GetByCodeAsync("nope"))
            .ReturnsAsync((FileRecord?)null);

        var result = await _sut.GetFileAsync("nope");

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetFile_ReturnsFailure_WhenExpired()
    {
        var file = new FileRecord
        {
            Code = "exp01",
            ExpiresAt = DateTime.UtcNow.AddHours(-1) // Already expired
        };
        _repoMock.Setup(r => r.GetByCodeAsync("exp01")).ReturnsAsync(file);

        var result = await _sut.GetFileAsync("exp01");

        Assert.False(result.IsSuccess);
        Assert.Contains("expired", result.Error, StringComparison.OrdinalIgnoreCase);
        _repoMock.Verify(r => r.IncrementDownloadCountAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetFile_ReturnsFailure_WhenDownloadLimitReached()
    {
        var file = new FileRecord
        {
            Code = "lim01",
            MaxDownloads = 5,
            DownloadCount = 5 // Limit reached
        };
        _repoMock.Setup(r => r.GetByCodeAsync("lim01")).ReturnsAsync(file);

        var result = await _sut.GetFileAsync("lim01");

        Assert.False(result.IsSuccess);
        _repoMock.Verify(r => r.IncrementDownloadCountAsync(It.IsAny<string>()), Times.Never);
    }

    // ── Delete ──────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsSuccess_WhenOwner()
    {
        var ownerId = Guid.NewGuid();
        var file = new FileRecord
        {
            Code = "del01",
            UploaderId = ownerId,
            StoragePath = "del01.jpg"
        };
        _repoMock.Setup(r => r.GetByCodeAsync("del01")).ReturnsAsync(file);

        var result = await _sut.DeleteAsync("del01", ownerId);

        Assert.True(result.IsSuccess);
        _storageMock.Verify(s => s.DeleteAsync("del01.jpg"), Times.Once);
        _repoMock.Verify(r => r.DeleteAsync(file), Times.Once);
    }

    [Fact]
    public async Task Delete_ReturnsFailure_WhenNotOwner()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        _repoMock.Setup(r => r.GetByCodeAsync("abc12"))
            .ReturnsAsync(new FileRecord { Code = "abc12", UploaderId = ownerId });

        var result = await _sut.DeleteAsync("abc12", otherUserId);

        Assert.False(result.IsSuccess);
        // Verify file was NOT deleted
        _storageMock.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
        _repoMock.Verify(r => r.DeleteAsync(It.IsAny<FileRecord>()), Times.Never);
    }

    [Fact]
    public async Task Delete_ReturnsFailure_WhenFileNotFound()
    {
        _repoMock.Setup(r => r.GetByCodeAsync("nope"))
            .ReturnsAsync((FileRecord?)null);

        var result = await _sut.DeleteAsync("nope", Guid.NewGuid());

        Assert.False(result.IsSuccess);
    }

    // ── Helper ──────────────────────────────────────────────

    private static IFormFile CreateMockFile(
        long size, string contentType = "image/jpeg", string fileName = "test.jpg")
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.Length).Returns(size);
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.FileName).Returns(fileName);
        mock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[size > 0 ? size : 1]));
        mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock.Object;
    }
}
```

### Test Checklist for Every Service Method

```
□ Success path returns Result<T>.Success with correct data
□ Each validation failure returns correct error message
□ Permission denied returns failure (not exception)
□ Not-found returns failure
□ Side effects (storage write, DB write) happen on success
□ Side effects DON'T happen when validation fails
□ Error messages are user-friendly, not technical
□ Expired/over-limit files are correctly rejected
```

---

## Integration Tests (WebApplicationFactory)

### Purpose

Integration tests verify the **full HTTP pipeline** — from HTTP request through controller, service, repository, and database, then back. They use an in-memory database so no external SQL Server is needed.

**This is required for Merit grade.**

### When to Write Integration Tests

```
WRITE AN INTEGRATION TEST WHEN:
├─ Verifying a full API endpoint works end-to-end
├─ Testing request/response serialization (JSON, multipart)
├─ Testing HTTP status codes match expectations
├─ Testing auth ([Authorize]) is enforced
├─ Testing middleware (error handling, CORS)
└─ Testing the DI container wires everything correctly
```

### Custom WebApplicationFactory

```csharp
// FileShare.IntegrationTests/CustomWebApplicationFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FileShare.Infrastructure.Persistence;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // ── Remove the real SQL Server DbContext ────────────
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // ── Add in-memory database for testing ─────────────
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("TestDb_" + Guid.NewGuid()));

            // ── Replace cloud storage with local for tests ─────
            // Storage is already LocalStorageProvider by default,
            // but ensure test uploads go to a temp directory
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });

        builder.UseEnvironment("Testing");
    }
}
```

> **Why `"TestDb_" + Guid.NewGuid()`?** Each test class gets a fresh database, preventing test pollution.

### Making `Program` Accessible

The `WebApplicationFactory<Program>` needs access to the `Program` class. Add this to the bottom of `Program.cs` or create a separate file:

```csharp
// backend/FileShare.API/Program.cs (at the very bottom)
// Make the implicit Program class accessible to integration tests
public partial class Program { }
```

### Integration Test: File Upload → Retrieve → Delete

```csharp
// FileShare.IntegrationTests/FilesEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

public class FilesEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public FilesEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Upload ──────────────────────────────────────────────

    [Fact]
    public async Task PostFile_Returns201_WhenFileValid()
    {
        // Arrange
        var content = CreateMultipartFile("hello world", "test.txt", "text/plain");

        // Act
        var response = await _client.PostAsync("/api/files", content);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("code", out var code));
        Assert.Equal(5, code.GetString()!.Length); // 5-char code
    }

    [Fact]
    public async Task PostFile_Returns400_WhenFileTooLarge()
    {
        // Arrange: 11 MB file
        var largeBytes = new byte[11 * 1024 * 1024];
        var content = CreateMultipartFile(largeBytes, "large.bin", "application/octet-stream");

        // Act
        var response = await _client.PostAsync("/api/files", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Retrieve ────────────────────────────────────────────

    [Fact]
    public async Task GetFile_Returns200_WhenFileExists()
    {
        // Arrange: Upload first
        var uploadContent = CreateMultipartFile("test data", "test.txt", "text/plain");
        var uploadResponse = await _client.PostAsync("/api/files", uploadContent);
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = uploadBody.GetProperty("code").GetString();

        // Act
        var response = await _client.GetAsync($"/api/files/{code}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetFileMeta_Returns200_WithMetadata()
    {
        // Arrange: Upload first
        var uploadContent = CreateMultipartFile("meta test", "meta.txt", "text/plain");
        var uploadResponse = await _client.PostAsync("/api/files", uploadContent);
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = uploadBody.GetProperty("code").GetString();

        // Act
        var response = await _client.GetAsync($"/api/files/{code}/meta");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("meta.txt", body.GetProperty("originalFilename").GetString());
        Assert.Equal("text/plain", body.GetProperty("mimeType").GetString());
    }

    [Fact]
    public async Task GetFile_Returns404_WhenCodeDoesNotExist()
    {
        var response = await _client.GetAsync("/api/files/ZZZZZ");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Delete ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteFile_Returns401_WhenNotAuthenticated()
    {
        // Arrange: Upload first (no auth required for upload)
        var uploadContent = CreateMultipartFile("delete me", "del.txt", "text/plain");
        var uploadResponse = await _client.PostAsync("/api/files", uploadContent);
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = uploadBody.GetProperty("code").GetString();

        // Act: Try to delete without auth token
        var response = await _client.DeleteAsync($"/api/files/{code}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Auth Flow ───────────────────────────────────────────

    [Fact]
    public async Task Register_Returns200_WithJwtToken()
    {
        var request = new { email = "test@example.com", password = "Password123!" };
        var response = await _client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body)); // Should contain JWT
    }

    [Fact]
    public async Task Login_Returns200_WithValidCredentials()
    {
        // Arrange: Register first
        var request = new { email = "login@example.com", password = "Password123!" };
        await _client.PostAsJsonAsync("/api/auth/register", request);

        // Act: Login
        var response = await _client.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsFailure_WithWrongPassword()
    {
        // Arrange: Register first
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = "wrong@example.com", password = "CorrectPass123!" });

        // Act: Login with wrong password
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "wrong@example.com", password = "WrongPass456!" });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Authenticated Delete ────────────────────────────────

    [Fact]
    public async Task DeleteFile_Returns204_WhenOwner()
    {
        // Step 1: Register and get token
        var regResponse = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = $"owner-{Guid.NewGuid()}@test.com", password = "Password123!" });
        var token = (await regResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token").GetString();

        // Step 2: Upload with auth token
        var uploadContent = CreateMultipartFile("owned file", "owned.txt", "text/plain");
        var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/files") { Content = uploadContent };
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var uploadResponse = await _client.SendAsync(uploadRequest);
        var code = (await uploadResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString();

        // Step 3: Delete with same auth token
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/files/{code}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var deleteResponse = await _client.SendAsync(deleteRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify file is gone
        var getResponse = await _client.GetAsync($"/api/files/{code}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    // ── Helpers ─────────────────────────────────────────────

    private static MultipartFormDataContent CreateMultipartFile(
        string textContent, string fileName, string contentType)
    {
        return CreateMultipartFile(
            System.Text.Encoding.UTF8.GetBytes(textContent),
            fileName, contentType);
    }

    private static MultipartFormDataContent CreateMultipartFile(
        byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }
}
```

---

## Running Tests

### Commands

```bash
# Run all tests (unit + integration)
dotnet test backend/FileShare.sln

# Run only unit tests
dotnet test backend/FileShare.Tests/FileShare.Tests.csproj

# Run only integration tests
dotnet test backend/FileShare.IntegrationTests/FileShare.IntegrationTests.csproj

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"

# Run a specific test
dotnet test --filter "Upload_ReturnsSuccess_WhenFileIsValid"

# Run in CI/CD (with trx output for GitHub Actions)
dotnet test backend/FileShare.sln --no-build --logger trx
```

### Inside Docker

```bash
docker-compose exec backend dotnet test FileShare.sln
```

---

## Test Decision Tree

```
WHAT TYPE OF TEST DO I NEED?

├─ Testing a single service method in isolation?
│  └─ UNIT TEST
│     Mock all dependencies (repository, storage, config)
│     Verify Result<T>.IsSuccess / .Error
│     Verify mock interactions (Times.Once, Times.Never)
│
├─ Testing an HTTP endpoint end-to-end?
│  └─ INTEGRATION TEST
│     Use WebApplicationFactory + in-memory DB
│     Send real HTTP request, check status code + body
│     No mocks — real DI container, real middleware
│
├─ Testing request validation ([Required], size limits)?
│  └─ INTEGRATION TEST
│     Model binding and [RequestSizeLimit] only work via HTTP
│
├─ Testing auth ([Authorize] attribute)?
│  └─ INTEGRATION TEST
│     JWT validation middleware only runs via HTTP
│
├─ Testing a React component?
│  └─ FRONTEND TEST (React Testing Library)
│     See fileshare-frontend/SKILL.md
│
└─ Testing the CI/CD pipeline?
   └─ Push to a feature branch and check GitHub Actions
```

---

## Common Mistakes

| ❌ Don't | ✅ Do | Why |
|---|---|---|
| Use a real database in unit tests | Mock with `Mock<IFileRepository>` | Unit tests must be fast and isolated |
| Use mocks in integration tests | Use `UseInMemoryDatabase` | Integration tests verify real wiring |
| Share database between test classes | Use `Guid.NewGuid()` in DB name | Prevents test pollution |
| Skip testing failure paths | Test every `Result<T>.Failure` branch | Failures are as important as successes |
| Test controller logic directly | Test through service (unit) or HTTP (integration) | Controllers should have no logic to test |
| Assert only status code | Assert response body too | Verify the full contract |
| Forget to verify side effects | Use `Mock.Verify()` for storage/repo calls | Ensure operations happened (or didn't) |
| Write tests after all code is done | Write tests alongside each feature | Catch bugs earlier, design better APIs |

---

## Project File References

### FileShare.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Moq" Version="4.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\FileShare.Application\FileShare.Application.csproj" />
    <ProjectReference Include="..\FileShare.Domain\FileShare.Domain.csproj" />
  </ItemGroup>
</Project>
```

### FileShare.IntegrationTests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\FileShare.API\FileShare.API.csproj" />
  </ItemGroup>
</Project>
```

---

## Cross-References

- **Service patterns being tested** → `fileshare-backend/SKILL.md`
- **Database schema and entities** → `fileshare-database/SKILL.md`
- **CI/CD test step** → `fileshare-devops/SKILL.md` (lint-and-test job)
- **Frontend testing** → `fileshare-frontend/SKILL.md`
