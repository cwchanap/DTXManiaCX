using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DTXMania.VideoRecorder.Obs;

namespace DTXMania.VideoRecorder.Tests.Obs;

public sealed class ObsProtocolTests
{
    [Fact]
    public void ComputeAuthentication_ShouldMatchObsV5Formula()
    {
        const string password = "correct horse battery staple";
        const string salt = "obs-salt";
        const string challenge = "obs-challenge";

        var secret = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        var expected = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));

        var actual = ObsProtocol.ComputeAuthentication(password, salt, challenge);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseRecordStatus_ShouldReadOutputActive()
    {
        const string response = """
            {"op":7,"d":{"requestType":"GetRecordStatus","requestId":"42","requestStatus":{"result":true,"code":100},"responseData":{"outputActive":true}}}
            """;

        var status = ObsProtocol.ParseRecordStatus(response);

        Assert.True(status.IsRecording);
    }

    [Fact]
    public void EnsureRequestSucceeded_ShouldAcceptSuccessfulResponse()
    {
        const string response = """
            {"op":7,"d":{"requestType":"StartRecord","requestId":"7","requestStatus":{"result":true,"code":100}}}
            """;

        ObsProtocol.EnsureRequestSucceeded(response, "StartRecord");
    }

    [Fact]
    public void EnsureRequestSucceeded_ShouldExposeObsFailureCodeAndComment()
    {
        const string response = """
            {"op":7,"d":{"requestType":"StartRecord","requestId":"7","requestStatus":{"result":false,"code":500,"comment":"Output is already active"}}}
            """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => ObsProtocol.EnsureRequestSucceeded(response, "StartRecord"));

        Assert.Contains("StartRecord", exception.Message);
        Assert.Contains("500", exception.Message);
        Assert.Contains("Output is already active", exception.Message);
    }

    [Fact]
    public void ParseStopRecordOutputPath_ShouldReturnOutputPathUnchanged()
    {
        const string outputPath = @"C:\recordings\cx-output-01.mkv";
        var response = JsonSerializer.Serialize(new
        {
            op = 7,
            d = new
            {
                requestType = "StopRecord",
                requestId = "8",
                requestStatus = new { result = true, code = 100 },
                responseData = new { outputPath }
            }
        });

        var actual = ObsProtocol.ParseStopRecordOutputPath(response);

        Assert.Equal(outputPath, actual);
    }

    [Fact]
    public void BuildIdentifyRequest_ShouldUseComputedAuthentication()
    {
        const string password = "pw";
        const string salt = "salt";
        const string challenge = "challenge";

        var payload = ObsProtocol.BuildIdentifyRequest(password, salt, challenge, rpcVersion: 1);
        using var document = JsonDocument.Parse(payload);
        var data = document.RootElement.GetProperty("d");

        Assert.Equal(1, document.RootElement.GetProperty("op").GetInt32());
        Assert.Equal(1, data.GetProperty("rpcVersion").GetInt32());
        Assert.Equal(
            ObsProtocol.ComputeAuthentication(password, salt, challenge),
            data.GetProperty("authentication").GetString());
    }

    [Theory]
    [InlineData(5, "GetRecordStatus")]
    [InlineData(6, "StopRecord")]
    public void ProtocolParsers_ShouldRejectMalformedOrWrongResponseKinds(int op, string requestType)
    {
        var response =
            $"{{\"op\":{op},\"d\":{{\"requestType\":\"{requestType}\",\"requestId\":\"1\",\"requestStatus\":{{\"result\":true,\"code\":100}}}}}}";

        Assert.Throws<InvalidOperationException>(() => ObsProtocol.ParseRecordStatus(response));
        Assert.Throws<InvalidOperationException>(() => ObsProtocol.ParseStopRecordOutputPath("not-json"));
    }

    [Fact]
    public void ParseStopRecordOutputPath_ShouldRejectMissingOutputPath()
    {
        const string response = """
            {"op":7,"d":{"requestType":"StopRecord","requestId":"8","requestStatus":{"result":true,"code":100},"responseData":{}}}
            """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => ObsProtocol.ParseStopRecordOutputPath(response));

        Assert.Contains("outputPath", exception.Message);
    }
}
