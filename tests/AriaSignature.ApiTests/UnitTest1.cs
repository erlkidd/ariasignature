using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AriaSignature.Api.Contracts;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AriaSignature.ApiTests;

public sealed class ApiEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions ApiJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly HttpClient _client;

    public ApiEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task StatusEndpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/v1/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DisksEndpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/v1/disks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DiskByUnknownId_ReturnsProblemNotFound()
    {
        var response = await _client.GetAsync($"/api/v1/disks/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task BackupCrudAndRunFlow_Works()
    {
        var sourceFile = Path.Combine(Path.GetTempPath(), $"aria_test_{Guid.NewGuid():N}.1CD");
        var destinationDir = Path.Combine(Path.GetTempPath(), $"aria_backups_{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(sourceFile, "test");
        Directory.CreateDirectory(destinationDir);

        var request = new UpsertBackupJobRequest
        {
            Name = "Тестовая задача",
            Type = BackupType.File,
            Source = sourceFile,
            Destination = destinationDir,
            ScheduleCron = "0 */5 * * * ?",
            RetentionCount = 3,
            IsEnabled = true
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/backups", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdJob = await createResponse.Content.ReadFromJsonAsync<BackupJob>(ApiJson);
        Assert.NotNull(createdJob);

        var getAllResponse = await _client.GetAsync("/api/v1/backups");
        Assert.Equal(HttpStatusCode.OK, getAllResponse.StatusCode);

        var runResponse = await _client.PostAsync($"/api/v1/backups/{createdJob!.Id}/run", content: null);
        Assert.Equal(HttpStatusCode.Accepted, runResponse.StatusCode);

        var logsResponse = await _client.GetAsync("/api/v1/backups/logs");
        Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);

        if (File.Exists(sourceFile))
        {
            File.Delete(sourceFile);
        }

        if (Directory.Exists(destinationDir))
        {
            Directory.Delete(destinationDir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateBackup_WithInvalidCron_ReturnsBadRequest()
    {
        var request = new UpsertBackupJobRequest
        {
            Name = "Некорректная задача",
            Type = BackupType.File,
            Source = "C:\\temp\\source.1CD",
            Destination = "C:\\temp\\backups",
            ScheduleCron = "bad cron",
            RetentionCount = 1,
            IsEnabled = true
        };

        var response = await _client.PostAsJsonAsync("/api/v1/backups", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMsSqlBackup_WithInvalidConnectionString_ReturnsBadRequest()
    {
        var request = new UpsertBackupJobRequest
        {
            Name = "MSSQL",
            Type = BackupType.MsSql,
            Source = "Server=.;User Id=sa;",
            Destination = "C:\\temp\\backups",
            ScheduleCron = "0 */5 * * * ?",
            RetentionCount = 1,
            IsEnabled = true
        };

        var response = await _client.PostAsJsonAsync("/api/v1/backups", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUnknownBackup_ReturnsProblemNotFound()
    {
        var response = await _client.DeleteAsync($"/api/v1/backups/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}