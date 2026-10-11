using Engine.Domain.Datasets;
using Engine.Protocol;
using Engine.Session;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Engine.Gateway.Tests;

public class ResourceLimitEndpointTests
{
    private static void FillToCapacity(SessionManager sessions)
    {
        while (sessions.ActiveSessionCount < sessions.MaxSessions)
            sessions.Create(new Matrix(1, 1));
    }

    [Fact]
    public async Task AtCapacity_NewSession_IsRejectedWithServerBusy()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var sessions = factory.Services.GetRequiredService<SessionManager>();
        FillToCapacity(sessions);

        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        await GatewayTestHelpers.SendCreateMatrixAsync(socket, 2, 2);

        var error = Assert.IsType<ErrorMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal("server-busy", error.Code);
        Assert.Equal(sessions.MaxSessions, sessions.ActiveSessionCount);
    }

    [Fact]
    public async Task AtCapacity_ConnectionThatAlreadyOwnsASession_CanStillReplaceIt()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var sessions = factory.Services.GetRequiredService<SessionManager>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);
        var first = await GatewayTestHelpers.CreateMatrixSessionAsync(socket);
        FillToCapacity(sessions);

        var second = await GatewayTestHelpers.CreateMatrixSessionAsync(socket);

        Assert.Null(sessions.TryGet(first));
        Assert.NotNull(sessions.TryGet(second));
        Assert.Equal(sessions.MaxSessions, sessions.ActiveSessionCount);
    }

    [Theory]
    [InlineData(1000, 1000)]
    [InlineData(100_000, 100_000)]
    public async Task OversizedDataset_IsRejected_AndConnectionStaysUsable(int rows, int columns)
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await GatewayTestHelpers.ConnectAsync(factory);

        await GatewayTestHelpers.SendCreateMatrixAsync(socket, rows, columns);
        var error = Assert.IsType<ErrorMessage>(await GatewayTestHelpers.ReceiveAsync(socket));
        Assert.Equal("invalid-create-session", error.Code);

        var recovered = await GatewayTestHelpers.CreateMatrixSessionAsync(socket);
        Assert.NotEqual(Guid.Empty, recovered);
    }
}