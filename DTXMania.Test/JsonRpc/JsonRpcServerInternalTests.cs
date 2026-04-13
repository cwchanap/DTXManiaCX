using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.JsonRpc;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Hosting;
using Moq;

namespace DTXMania.Test.JsonRpc;

[Trait("Category", "Unit")]
public class JsonRpcServerInternalTests
{
    [Fact]
    public async Task HandleGetGameState_WhenGameIsRunning_ShouldReturnSuccessResponse()
    {
        var gameApi = CreateGameApi();
        gameApi
            .Setup(api => api.GetGameStateAsync())
            .ReturnsAsync(new GameState { Score = 42, CurrentStage = "Title" });
        var server = new JsonRpcServer(gameApi.Object);

        var response = await ReflectionHelpers.InvokePrivateMethodAsync<JsonRpcResponse>(
            server,
            "HandleGetGameState",
            new JsonRpcRequest { Id = 7, Method = "getGameState" });

        Assert.Null(response!.Error);
        Assert.Equal(7, response.Id);
        using var resultDocument = JsonDocument.Parse(JsonSerializer.Serialize(response.Result));
        Assert.Equal(42, resultDocument.RootElement.GetProperty("Score").GetInt32());
        Assert.Equal("Title", resultDocument.RootElement.GetProperty("CurrentStage").GetString());
    }

    [Fact]
    public async Task HandleGetWindowInfo_WhenGameIsRunning_ShouldReturnSuccessResponse()
    {
        var gameApi = CreateGameApi();
        gameApi
            .Setup(api => api.GetWindowInfoAsync())
            .ReturnsAsync(new GameWindowInfo { Width = 1280, Height = 720, Title = "DTXManiaCX" });
        var server = new JsonRpcServer(gameApi.Object);

        var response = await ReflectionHelpers.InvokePrivateMethodAsync<JsonRpcResponse>(
            server,
            "HandleGetWindowInfo",
            new JsonRpcRequest { Id = 11, Method = "getWindowInfo" });

        Assert.Null(response!.Error);
        Assert.Equal(11, response.Id);
        using var resultDocument = JsonDocument.Parse(JsonSerializer.Serialize(response.Result));
        Assert.Equal(1280, resultDocument.RootElement.GetProperty("Width").GetInt32());
        Assert.Equal("DTXManiaCX", resultDocument.RootElement.GetProperty("Title").GetString());
    }

    [Fact]
    public async Task HandleSendInput_WhenParamsAreMissing_ShouldReturnInvalidParams()
    {
        var server = new JsonRpcServer(CreateGameApi().Object);

        var response = await ReflectionHelpers.InvokePrivateMethodAsync<JsonRpcResponse>(
            server,
            "HandleSendInput",
            new JsonRpcRequest { Id = 3, Method = "sendInput" });

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response!.Error!.Code);
        Assert.Equal("Input parameters are required", response.Error.Message);
    }

    [Fact]
    public async Task HandleSendInput_WhenParamsAreNotJsonElement_ShouldReturnInvalidParams()
    {
        var server = new JsonRpcServer(CreateGameApi().Object);

        var response = await ReflectionHelpers.InvokePrivateMethodAsync<JsonRpcResponse>(
            server,
            "HandleSendInput",
            new JsonRpcRequest { Id = 4, Method = "sendInput", Params = new object() });

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response!.Error!.Code);
        Assert.Equal("Invalid input format", response.Error.Message);
    }

    [Fact]
    public async Task HandleSendInput_WhenGameInputJsonIsInvalid_ShouldReturnInvalidParams()
    {
        using var jsonDocument = JsonDocument.Parse("{\"type\":\"bad\",\"data\":\"Enter\"}");
        var server = new JsonRpcServer(CreateGameApi().Object);

        var response = await ReflectionHelpers.InvokePrivateMethodAsync<JsonRpcResponse>(
            server,
            "HandleSendInput",
            new JsonRpcRequest { Id = 4, Method = "sendInput", Params = jsonDocument.RootElement.Clone() });

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response!.Error!.Code);
        Assert.Contains("Invalid input format", response.Error.Message);
    }

    [Fact]
    public async Task HandleTakeScreenshot_WhenCaptureFails_ShouldReturnInternalError()
    {
        var gameApi = CreateGameApi();
        gameApi.Setup(api => api.TakeScreenshotAsync()).ReturnsAsync((byte[]?)null);
        var server = new JsonRpcServer(gameApi.Object);

        var response = await ReflectionHelpers.InvokePrivateMethodAsync<JsonRpcResponse>(
            server,
            "HandleTakeScreenshot",
            new JsonRpcRequest { Id = 5, Method = "takeScreenshot" });

        Assert.Equal(JsonRpcErrorCodes.InternalError, response!.Error!.Code);
        Assert.Contains("Screenshot capture failed", response.Error.Message);
    }

    [Fact]
    public async Task HandleTakeScreenshot_WhenCaptureSucceeds_ShouldReturnBase64EncodedPng()
    {
        var gameApi = CreateGameApi();
        gameApi.Setup(api => api.TakeScreenshotAsync()).ReturnsAsync(new byte[] { 1, 2, 3 });
        var server = new JsonRpcServer(gameApi.Object);

        var response = await ReflectionHelpers.InvokePrivateMethodAsync<JsonRpcResponse>(
            server,
            "HandleTakeScreenshot",
            new JsonRpcRequest { Id = 6, Method = "takeScreenshot" });

        Assert.Null(response!.Error);
        var resultJson = JsonSerializer.Serialize(response.Result);
        Assert.Contains("\"imageData\":\"AQID\"", resultJson);
        Assert.Contains("\"mimeType\":\"image/png\"", resultJson);
    }

    [Fact]
    public async Task HandleChangeStage_WhenParamsAreMissing_ShouldReturnInvalidParams()
    {
        var server = new JsonRpcServer(CreateGameApi().Object);

        var response = await ReflectionHelpers.InvokePrivateMethodAsync<JsonRpcResponse>(
            server,
            "HandleChangeStage",
            new JsonRpcRequest { Id = 9, Method = "changeStage" });

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response!.Error!.Code);
        Assert.Equal("stageName parameter is required", response.Error.Message);
    }

    [Fact]
    public async Task RouteMethodCall_WhenMethodIsPing_ShouldReturnPong()
    {
        var server = new JsonRpcServer(CreateGameApi().Object);

        var response = await ReflectionHelpers.InvokePrivateMethodAsync<JsonRpcResponse>(
            server,
            "RouteMethodCall",
            new JsonRpcRequest { Id = 15, Method = "ping" });

        Assert.Null(response!.Error);
        Assert.Equal(15, response.Id);
        var resultJson = JsonSerializer.Serialize(response.Result);
        Assert.Contains("\"pong\":true", resultJson);
        Assert.Contains("\"timestamp\":", resultJson);
    }

    [Fact]
    public async Task RouteMethodCall_WhenHandlerThrows_ShouldReturnInternalErrorWithCorrelationId()
    {
        var gameApi = CreateGameApi();
        gameApi
            .Setup(api => api.GetGameStateAsync())
            .ThrowsAsync(new InvalidOperationException("boom"));
        var server = new JsonRpcServer(gameApi.Object);

        var response = await ReflectionHelpers.InvokePrivateMethodAsync<JsonRpcResponse>(
            server,
            "RouteMethodCall",
            new JsonRpcRequest { Id = 17, Method = "getGameState" });

        Assert.Equal(JsonRpcErrorCodes.InternalError, response!.Error!.Code);
        Assert.Contains("CorrelationId:", response.Error.Message);
    }

    [Theory]
    [InlineData("secret", "secret", true)]
    [InlineData("secret", "public", false)]
    [InlineData("secret", "secret-value", false)]
    public void ConstantTimeEquals_ShouldRespectLengthAndContent(string left, string right, bool expected)
    {
        var result = InvokePrivateStatic<bool>("ConstantTimeEquals", left, right);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CancelNoThrow_WithDisposedTokenSource_ShouldSuppressObjectDisposedException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Dispose();

        var exception = Record.Exception(() => InvokePrivateStatic("CancelNoThrow", cancellation));

        Assert.Null(exception);
    }

    [Fact]
    public void CancelNoThrow_WithThrowingCallback_ShouldSuppressAggregateException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Token.Register(() => throw new InvalidOperationException("boom"));

        var exception = Record.Exception(() => InvokePrivateStatic("CancelNoThrow", cancellation));

        Assert.Null(exception);
    }

    [Fact]
    public void DisposeManagedResourcesSynchronously_NoLock_ShouldDisposeHostAndClearState()
    {
        var server = new JsonRpcServer(CreateGameApi().Object);
        var host = new Mock<IHost>();
        using var cancellation = new CancellationTokenSource();
        ReflectionHelpers.SetPrivateField(server, "_host", host.Object);
        ReflectionHelpers.SetPrivateField(server, "_cancellationTokenSource", cancellation);
        ReflectionHelpers.SetPrivateField(server, "_isRunning", true);

        InvokePrivateInstance(server, "DisposeManagedResourcesSynchronously_NoLock");

        host.Verify(value => value.Dispose(), Times.Once);
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(server, "_host"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(server, "_cancellationTokenSource"));
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task StopAndDisposeHostAsync_NoLock_ShouldStopDisposeHostAndClearState()
    {
        var server = new JsonRpcServer(CreateGameApi().Object);
        var host = new Mock<IHost>();
        host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var cancellation = new CancellationTokenSource();
        ReflectionHelpers.SetPrivateField(server, "_host", host.Object);
        ReflectionHelpers.SetPrivateField(server, "_cancellationTokenSource", cancellation);
        ReflectionHelpers.SetPrivateField(server, "_isRunning", true);

        var task = (Task)ReflectionHelpers.InvokePrivateMethod(server, "StopAndDisposeHostAsync_NoLock")!;
        await task;

        host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Verify(value => value.Dispose(), Times.Once);
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(server, "_host"));
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task StopAsync_WhenRunning_ShouldStopDisposeHostAndClearState()
    {
        var server = new JsonRpcServer(CreateGameApi().Object);
        var host = new Mock<IHost>();
        using var cancellation = new CancellationTokenSource();
        var cancelled = false;
        cancellation.Token.Register(() => cancelled = true);
        host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ReflectionHelpers.SetPrivateField(server, "_host", host.Object);
        ReflectionHelpers.SetPrivateField(server, "_cancellationTokenSource", cancellation);
        ReflectionHelpers.SetPrivateField(server, "_isRunning", true);

        await server.StopAsync();

        Assert.True(cancelled);
        host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Verify(value => value.Dispose(), Times.Once);
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(server, "_host"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(server, "_cancellationTokenSource"));
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task StopAsync_WhenHostDisposeThrows_ShouldSwallowDisposeExceptionAndClearState()
    {
        var server = new JsonRpcServer(CreateGameApi().Object);
        var host = new Mock<IHost>();
        using var cancellation = new CancellationTokenSource();
        host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        host.Setup(value => value.Dispose()).Throws(new InvalidOperationException("dispose failed"));
        ReflectionHelpers.SetPrivateField(server, "_host", host.Object);
        ReflectionHelpers.SetPrivateField(server, "_cancellationTokenSource", cancellation);
        ReflectionHelpers.SetPrivateField(server, "_isRunning", true);

        var exception = await Record.ExceptionAsync(() => server.StopAsync());

        Assert.Null(exception);
        host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Verify(value => value.Dispose(), Times.Once);
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(server, "_host"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(server, "_cancellationTokenSource"));
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task StopAsync_WhenHostStopThrows_ShouldRethrowAfterCleanup()
    {
        var server = new JsonRpcServer(CreateGameApi().Object);
        var host = new Mock<IHost>();
        using var cancellation = new CancellationTokenSource();
        host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("stop failed"));
        ReflectionHelpers.SetPrivateField(server, "_host", host.Object);
        ReflectionHelpers.SetPrivateField(server, "_cancellationTokenSource", cancellation);
        ReflectionHelpers.SetPrivateField(server, "_isRunning", true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => server.StopAsync());

        Assert.Equal("stop failed", exception.Message);
        host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Verify(value => value.Dispose(), Times.Once);
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(server, "_host"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(server, "_cancellationTokenSource"));
        Assert.False(server.IsRunning);
    }

    [Fact]
    public void DisposeLifecycleSemaphoreOnce_WhenCalledTwice_ShouldBeIdempotent()
    {
        var server = new JsonRpcServer(CreateGameApi().Object);

        var exception = Record.Exception(() =>
        {
            InvokePrivateInstance(server, "DisposeLifecycleSemaphoreOnce");
            InvokePrivateInstance(server, "DisposeLifecycleSemaphoreOnce");
        });

        Assert.Null(exception);
        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(server, "_lifecycleSemaphoreDisposed"));
    }

    private static Mock<IGameApi> CreateGameApi()
    {
        var gameApi = new Mock<IGameApi>();
        gameApi.SetupGet(api => api.IsRunning).Returns(true);
        return gameApi;
    }

    private static void InvokePrivateInstance(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, args);
    }

    private static void InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = typeof(JsonRpcServer).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(null, args);
    }

    private static T InvokePrivateStatic<T>(string methodName, params object?[] args)
    {
        var method = typeof(JsonRpcServer).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, args)!;
    }
}
