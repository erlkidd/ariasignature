using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AriaSignature.ApiTests;

public sealed class ApiEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
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
}