using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Engine.Gateway.Tests;

public class OperationsEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetOperations_ReturnsEveryRegisteredOperation()
    {
        var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<List<OperationDto>>("/operations");

        Assert.NotNull(result);
        Assert.Equal(60, result!.Count);
        Assert.Contains(result, o => o.Id == "matrix-to-graph" && o.DatasetType == "matrix");
    }
    [Fact]
    public async Task GetOperations_EveryEntryCarriesExampleOptions()
    {
        var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/operations"));

        foreach (var entry in document.RootElement.EnumerateArray())
            Assert.Equal(JsonValueKind.Object, entry.GetProperty("exampleOptions").ValueKind);
    }

    private sealed record OperationDto(string Id, string DatasetType);
}