using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Present.Services;

namespace Present.Tests;

public sealed class RemoteControlServerTests : IDisposable
{
    private readonly List<RemoteControlServer> _servers = [];
    private readonly HttpClient _http = new();

    [Fact]
    public async Task Status_ReturnsJsonPayload()
    {
        using var testServer = StartServer(_ => { }, statusFactory: () => new PresentationStatus
        {
            CurrentIndex = 2,
            SlideCount = 5,
            IsPlaying = true,
            CurrentUrl = "https://example.com",
            ZoomFactor = 1.5
        });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}/status");
        var payload = await response.Content.ReadFromJsonAsync<PresentationStatus>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.CurrentIndex);
        Assert.Equal(5, payload.SlideCount);
        Assert.True(payload.IsPlaying);
    }

    [Fact]
    public async Task Next_Endpoint_InvokesCallback()
    {
        var called = 0;
        using var testServer = StartServer(s => s.OnNext = () => Interlocked.Increment(ref called));

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}/next");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, Volatile.Read(ref called));
    }

    [Fact]
    public async Task Unknown_Endpoint_ReturnsNotFound()
    {
        using var testServer = StartServer(_ => { });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Scroll_WithoutDy_DoesNotInvokeCallback()
    {
        var called = 0;
        using var testServer = StartServer(s =>
        {
            s.OnScroll = _ => Interlocked.Increment(ref called);
        });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}/scroll");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, Volatile.Read(ref called));
    }

    [Fact]
    public void Stop_And_Dispose_AreIdempotent()
    {
        using var testServer = StartServer(_ => { });
        var server = testServer.Server;

        server.Stop();
        server.Stop();
        server.Dispose();
        server.Dispose();
    }

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            try { server.Dispose(); } catch { }
        }

        _http.Dispose();
    }

    private TestServer StartServer(Action<RemoteControlServer> configure, Func<PresentationStatus>? statusFactory = null)
    {
        var port = GetFreePort();
        var server = new RemoteControlServer(port)
        {
            GetStatus = statusFactory ?? (() => new PresentationStatus())
        };

        configure(server);
        server.Start();
        _servers.Add(server);

        return new TestServer(server, port);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record TestServer(RemoteControlServer Server, int Port) : IDisposable
    {
        public void Dispose()
        {
            Server.Dispose();
        }
    }
}
