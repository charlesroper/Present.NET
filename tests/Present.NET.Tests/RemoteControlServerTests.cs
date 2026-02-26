using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Diagnostics;
using Present.NET.Services;

namespace Present.NET.Tests;

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

    [Theory]
    [InlineData("/next")]
    [InlineData("/prev")]
    [InlineData("/play")]
    [InlineData("/stop")]
    [InlineData("/zoomin")]
    [InlineData("/zoomout")]
    public async Task Command_Endpoint_InvokesExpectedCallback(string path)
    {
        var next = 0;
        var prev = 0;
        var play = 0;
        var stop = 0;
        var zoomIn = 0;
        var zoomOut = 0;

        using var testServer = StartServer(s =>
        {
            s.OnNext = () => Interlocked.Increment(ref next);
            s.OnPrev = () => Interlocked.Increment(ref prev);
            s.OnPlay = () => Interlocked.Increment(ref play);
            s.OnStop = () => Interlocked.Increment(ref stop);
            s.OnZoomIn = () => Interlocked.Increment(ref zoomIn);
            s.OnZoomOut = () => Interlocked.Increment(ref zoomOut);
        });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}{path}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(path == "/next" ? 1 : 0, Volatile.Read(ref next));
        Assert.Equal(path == "/prev" ? 1 : 0, Volatile.Read(ref prev));
        Assert.Equal(path == "/play" ? 1 : 0, Volatile.Read(ref play));
        Assert.Equal(path == "/stop" ? 1 : 0, Volatile.Read(ref stop));
        Assert.Equal(path == "/zoomin" ? 1 : 0, Volatile.Read(ref zoomIn));
        Assert.Equal(path == "/zoomout" ? 1 : 0, Volatile.Read(ref zoomOut));
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

    [Theory]
    [InlineData(200)]
    [InlineData(-200)]
    public async Task Scroll_WithDy_InvokesCallback(int dy)
    {
        var observed = int.MinValue;
        using var testServer = StartServer(s =>
        {
            s.OnScroll = value => observed = value;
        });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}/scroll?dy={dy}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(dy, observed);
    }

    [Fact]
    public async Task Status_Response_IncludesCorsHeader()
    {
        using var testServer = StartServer(_ => { });

        var response = await _http.GetAsync($"http://localhost:{testServer.Port}/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Contains("*", values!);
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

    [Fact]
    public void Start_AfterDispose_ThrowsObjectDisposedException()
    {
        var server = new RemoteControlServer(GetFreePort());

        server.Dispose();

        Assert.Throws<ObjectDisposedException>(() => server.Start());
    }

    [Fact]
    public async Task Stop_ReturnsPromptly_DuringSlowInFlightRequest()
    {
        using var testServer = StartServer(s =>
        {
            s.OnNext = () => Thread.Sleep(1500);
        });

        var request = _http.GetAsync($"http://localhost:{testServer.Port}/next");
        await Task.Delay(100);

        var sw = Stopwatch.StartNew();
        testServer.Server.Stop();
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
        await request;
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
