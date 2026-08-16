using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using DTXManiaCX.MCP.Server.Services;
using DTXManiaCX.MCP.Server.Tools;

namespace DTXManiaCX.MCP.Tests.Server;

public sealed class GameInteractionMcpToolHandlersTests
{
    [Fact]
    public async Task InvalidClient_HandlerResultCarriesCamelCaseStructuredJsonObject()
    {
        using var service = CreateService("http://127.0.0.1:1/jsonrpc");
        var handlers = new GameInteractionMcpToolHandlers(service);

        var result = await handlers.ClickAsync(string.Empty, 10, 20);

        Assert.True(result.IsError);
        Assert.True(result.StructuredContent.HasValue);

        var structuredContent = result.StructuredContent.Value;
        Assert.Equal(JsonValueKind.Object, structuredContent.ValueKind);
        Assert.Equal("click", structuredContent.GetProperty("action").GetString());
        Assert.False(structuredContent.TryGetProperty("Action", out _));
    }

    [Fact]
    public async Task Screenshot_HandlerPreservesDecodedPngBytesAndMimeType()
    {
        byte[] expectedPng =
        new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0xF0,
            0x1F, 0x00, 0x05, 0x00, 0x01, 0xFF, 0x89, 0x99,
            0x3D, 0x1D, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
            0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
        };
        const string mimeType = "image/png";
        var encodedPng = Convert.ToBase64String(expectedPng);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var fakeServerTask = ServeScreenshotResponseAsync(listener, encodedPng, mimeType);

        try
        {
            using var service = CreateService($"http://127.0.0.1:{endpoint.Port}/jsonrpc");
            var handlers = new GameInteractionMcpToolHandlers(service);

            var result = await handlers.TakeScreenshotAsync("default");
            await fakeServerTask;

            Assert.False(result.IsError);
            var image = Assert.IsType<ImageContentBlock>(Assert.Single(result.Content.Skip(1)));
            Assert.Equal(mimeType, image.MimeType);
            Assert.Equal(expectedPng, image.DecodedData.ToArray());
            Assert.Equal(encodedPng, Encoding.UTF8.GetString(image.Data.Span));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Screenshot_MalformedBase64WithSuccessTrue_ReturnsErrorInsteadOfThrowing()
    {
        const string malformedBase64 = "not-valid-base64!!!";
        const string mimeType = "image/png";

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var fakeServerTask = ServeScreenshotResponseAsync(listener, malformedBase64, mimeType);

        try
        {
            using var service = CreateService($"http://127.0.0.1:{endpoint.Port}/jsonrpc");
            var handlers = new GameInteractionMcpToolHandlers(service);

            var result = await handlers.TakeScreenshotAsync("default");
            await fakeServerTask;

            Assert.True(result.IsError);
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
            Assert.Contains("malformed Base64", text.Text);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static GameInteractionService CreateService(string gameApiUrl)
    {
        return new GameInteractionService(
            NullLogger<GameInteractionService>.Instance,
            NullLoggerFactory.Instance,
            new GameInteractionOptions { GameApiUrl = gameApiUrl });
    }

    private static async Task ServeScreenshotResponseAsync(
        TcpListener listener,
        string encodedPng,
        string mimeType)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync();
        Assert.StartsWith("POST /jsonrpc", requestLine);

        var contentLength = 0;
        string? headerLine;
        while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync()))
        {
            if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(headerLine["Content-Length:".Length..].Trim());
            }
        }

        var body = new char[contentLength];
        var read = 0;
        while (read < body.Length)
        {
            var count = await reader.ReadAsync(body.AsMemory(read, body.Length - read));
            if (count == 0)
                break;

            read += count;
        }

        using var request = JsonDocument.Parse(body.AsMemory(0, read));
        Assert.Equal("takeScreenshot", request.RootElement.GetProperty("method").GetString());
        var response = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = request.RootElement.GetProperty("id").GetInt32(),
            result = new { imageData = encodedPng, mimeType }
        });
        var responseBytes = Encoding.UTF8.GetBytes(response);
        var responseHeaders = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {responseBytes.Length}\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(responseHeaders);
        await stream.WriteAsync(responseBytes);
    }
}
