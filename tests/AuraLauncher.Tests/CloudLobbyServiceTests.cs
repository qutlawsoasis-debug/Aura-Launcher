using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Services.Implementations;
using Xunit;

namespace AuraLauncher.Tests;

/// <summary>
/// Облачные миры: лаунчер ходит только в lobby-api и никогда не шлёт секрет менеджера.
/// </summary>
public class CloudLobbyServiceTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public readonly List<(HttpRequestMessage Request, string Body)> Calls = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return Respond(request);
        }
    }

    private static (CloudLobbyService Service, RecordingHandler Handler) Make()
    {
        var handler = new RecordingHandler();
        var service = new CloudLobbyService(null, new HttpClient(handler), "https://lobby.test/");
        return (service, handler);
    }

    private static void AssertNoSecret(HttpRequestMessage req)
    {
        Assert.False(req.Headers.Contains("X-Aura-Secret"));
        Assert.DoesNotContain(req.Headers, h => h.Key.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AllCallsGoToLobbyApiWithoutTheSecret()
    {
        var (service, handler) = Make();

        await service.GetWorldsAsync("Алекс");
        await service.CreateOrStartWorldAsync("Steve", "Мир");
        await service.StopWorldAsync("w1", 25601);
        await service.ShareWorldAsync("Steve", "w1", "Alex");
        await service.DeleteWorldAsync("Steve", "w1");

        var urls = handler.Calls.Select(c => c.Request.RequestUri!.AbsoluteUri).ToArray();
        Assert.Equal(new[]
        {
            "https://lobby.test/api/cloud/worlds?player=%D0%90%D0%BB%D0%B5%D0%BA%D1%81",
            "https://lobby.test/api/cloud/create",
            "https://lobby.test/api/cloud/stop",
            "https://lobby.test/api/cloud/share",
            "https://lobby.test/api/cloud/delete",
        }, urls);
        foreach (var call in handler.Calls)
        {
            AssertNoSecret(call.Request);
            Assert.Equal("lobby.test", call.Request.RequestUri!.Host);
        }
        Assert.Contains("\"owner\":\"Steve\"", handler.Calls[1].Body);
    }

    [Fact]
    public async Task UploadTakesATicketThenSendsTheZipWithIt()
    {
        var (service, handler) = Make();
        handler.Respond = req => req.RequestUri!.AbsolutePath.EndsWith("/upload-ticket")
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"uploadUrl\":\"http://manager.test:25571/worlds/upload\",\"token\":\"abc123\",\"expires\":1900000000}", Encoding.UTF8, "application/json")
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"worldId\":\"w9\"}", Encoding.UTF8, "application/json")
            };

        var zip = Path.Combine(Path.GetTempPath(), $"aura-cloud-test-{Guid.NewGuid():N}.zip");
        await File.WriteAllBytesAsync(zip, new byte[] { 0x50, 0x4B, 0x05, 0x06 });
        try
        {
            var item = await service.UploadWorldAsync("Steve", "Мой мир", zip, "1.21.1");
            Assert.NotNull(item);
        }
        finally
        {
            File.Delete(zip);
        }

        Assert.Equal(2, handler.Calls.Count);
        Assert.Equal("https://lobby.test/api/cloud/upload-ticket", handler.Calls[0].Request.RequestUri!.ToString());
        Assert.Contains("\"worldName\":\"Мой мир\"", System.Text.RegularExpressions.Regex.Unescape(handler.Calls[0].Body));

        var upload = handler.Calls[1].Request;
        Assert.Equal("http://manager.test:25571/worlds/upload", upload.RequestUri!.ToString());
        Assert.Equal("abc123", upload.Headers.GetValues("X-Upload-Token").Single());
        Assert.Equal("1900000000", upload.Headers.GetValues("X-Upload-Expires").Single());
        Assert.Equal("Steve", upload.Headers.GetValues("X-Owner").Single());
        foreach (var call in handler.Calls) AssertNoSecret(call.Request);
    }

    [Fact]
    public async Task NoTicketMeansNoUpload()
    {
        var (service, handler) = Make();
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
        var zip = Path.Combine(Path.GetTempPath(), $"aura-cloud-test-{Guid.NewGuid():N}.zip");
        await File.WriteAllBytesAsync(zip, new byte[] { 1 });
        try
        {
            Assert.Null(await service.UploadWorldAsync("Steve", "Мир", zip));
        }
        finally
        {
            File.Delete(zip);
        }
        Assert.Single(handler.Calls);
    }
}
