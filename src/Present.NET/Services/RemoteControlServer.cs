using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Present.NET.Services;

/// <summary>
/// Embedded HTTP server on port 9123 providing remote control of the presentation.
/// Endpoints: GET /next, /prev, /play, /stop, /zoomin, /zoomout, /scroll?dy=N, /status, /
/// </summary>
public class RemoteControlServer : IDisposable
{
    private readonly int _port;
    private CancellationTokenSource? _cts;
    private WebApplication? _app;
    private bool _disposed;
    private bool _started;
    private Task _shutdownTask = Task.CompletedTask;
    private readonly object _lifecycleLock = new();

    // Actions dispatched to the UI
    public Action? OnNext { get; set; }
    public Action? OnPrev { get; set; }
    public Action? OnPlay { get; set; }
    public Action? OnStop { get; set; }
    public Action? OnZoomIn { get; set; }
    public Action? OnZoomOut { get; set; }
    public Action<int>? OnScroll { get; set; }

    // Status provider
    public Func<PresentationStatus>? GetStatus { get; set; }

    public RemoteControlServer(int port = 9123)
    {
        _port = port;
    }

    public void Start()
    {
        // Await prior shutdown if needed
        _shutdownTask.GetAwaiter().GetResult();

        lock (_lifecycleLock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RemoteControlServer));
            if (_started) return;

            _cts = new CancellationTokenSource();
            _app = CreateWebApp();
            _app.StartAsync(_cts.Token).GetAwaiter().GetResult();
            _started = true;
        }
    }

    public void Stop()
    {
        WebApplication? appToStop;
        CancellationTokenSource? ctsToCancel;

        lock (_lifecycleLock)
        {
            if (!_started) return;
            _started = false;

            appToStop = _app;
            _app = null;

            ctsToCancel = _cts;
            _cts = null;
        }

        try { ctsToCancel?.Cancel(); } catch { }

        if (appToStop != null)
        {
            _shutdownTask = Task.Run(async () =>
            {
                try
                {
                    await appToStop.StopAsync();
                }
                catch { }
                finally
                {
                    try { await appToStop.DisposeAsync(); } catch { }
                }
            });
        }

        ctsToCancel?.Dispose();
    }

    private WebApplication CreateWebApp()
    {
        var options = new WebApplicationOptions { Args = [] };
        var builder = WebApplication.CreateSlimBuilder(options);
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(server =>
        {
            server.ListenAnyIP(_port);
        });

        var app = builder.Build();

        app.MapGet("/", () => Results.Content(GetControlPageHtml(), "text/html; charset=utf-8"));
        app.MapGet("/next", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnNext));
        app.MapGet("/prev", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnPrev));
        app.MapGet("/play", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnPlay));
        app.MapGet("/stop", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnStop));
        app.MapGet("/zoomin", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnZoomIn));
        app.MapGet("/zoomout", (HttpContext ctx) => ExecuteAndReturnStatus(ctx, OnZoomOut));
        app.MapGet("/scroll", (HttpContext ctx) =>
        {
            if (TryGetIntQueryParam(ctx.Request, "dy", out var dy))
                OnScroll?.Invoke(dy);

            return BuildStatusResult(ctx);
        });
        app.MapGet("/status", (HttpContext ctx) => BuildStatusResult(ctx));

        return app;
    }

    private IResult ExecuteAndReturnStatus(HttpContext ctx, Action? callback)
    {
        callback?.Invoke();
        return BuildStatusResult(ctx);
    }

    private IResult BuildStatusResult(HttpContext ctx)
    {
        ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
        var status = GetStatus?.Invoke() ?? new PresentationStatus();
        return Results.Json(status);
    }

    private static bool TryGetIntQueryParam(HttpRequest request, string param, out int value)
    {
        value = 0;
        return int.TryParse(request.Query[param], out value);
    }

    private static string GetControlPageHtml() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">
          <title>Present.NET Remote</title>
          <style>
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body {
              background: #111;
              color: #fff;
              font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
              min-height: 100vh;
              display: flex;
              flex-direction: column;
              align-items: center;
              justify-content: center;
              padding: 20px;
            }
            h1 { font-size: 1.4em; margin-bottom: 8px; }
            #slide-info {
              font-size: 1.1em;
              color: #aaa;
              margin-bottom: 20px;
              min-height: 1.4em;
            }
            #slide-info span { color: #0af; font-weight: bold; }
            .grid {
              display: grid;
              grid-template-columns: 1fr 1fr;
              gap: 12px;
              width: 100%;
              max-width: 360px;
            }
            button {
              background: #2a2a2a;
              color: #fff;
              border: 1px solid #444;
              border-radius: 14px;
              padding: 22px 12px;
              font-size: 1.1em;
              cursor: pointer;
              touch-action: manipulation;
              -webkit-tap-highlight-color: transparent;
              transition: background 0.1s;
            }
            button:active { background: #444; }
            button.wide { grid-column: span 2; }
            button.play { background: #0a5; border-color: #0c6; }
            button.play:active { background: #0c6; }
            button.stop { background: #733; border-color: #944; }
            button.stop:active { background: #955; }
            .scroll-row {
              grid-column: span 2;
              display: grid;
              grid-template-columns: 1fr 1fr;
              gap: 12px;
            }
            #status-bar {
              margin-top: 20px;
              font-size: 0.8em;
              color: #555;
            }
            #status-bar.connected { color: #0a5; }
            #status-bar.error { color: #c44; }
          </style>
        </head>
        <body>
          <h1>Present.NET Remote</h1>
          <div id="slide-info">Connecting...</div>
          <div class="grid">
            <button onclick="cmd('prev')">Prev</button>
            <button onclick="cmd('next')">Next</button>
            <button class="wide play" onclick="cmd('play')">Play Fullscreen</button>
            <button class="wide stop" onclick="cmd('stop')">Stop</button>
            <button onclick="cmd('zoomin')">Zoom In</button>
            <button onclick="cmd('zoomout')">Zoom Out</button>
            <button onclick="cmd('scroll?dy=-200')">Scroll Up</button>
            <button onclick="cmd('scroll?dy=200')">Scroll Down</button>
          </div>
          <div id="status-bar">Connecting...</div>

          <script>
            function cmd(action) {
              fetch('/' + action)
                .then(r => r.json())
                .then(updateUI)
                .catch(e => setError(e));
            }

            function updateUI(s) {
              if (!s) return;
              const total = s.slideCount || 0;
              const idx = (s.currentIndex || 0) + 1;
              const playing = s.isPlaying;
              document.getElementById('slide-info').innerHTML =
                total > 0
                  ? 'Slide <span>' + idx + '</span> of <span>' + total + '</span>'
                    + (playing ? ' <span style="color:#0a5">Playing</span>' : '')
                  : 'No slides loaded';
              document.getElementById('status-bar').textContent = 'Connected';
              document.getElementById('status-bar').className = 'connected';
            }

            function setError(e) {
              document.getElementById('status-bar').textContent = 'Error: ' + e;
              document.getElementById('status-bar').className = 'error';
            }

            function poll() {
              fetch('/status')
                .then(r => r.json())
                .then(updateUI)
                .catch(e => {
                  document.getElementById('status-bar').textContent = 'Disconnected';
                  document.getElementById('status-bar').className = 'error';
                });
            }

            setInterval(poll, 2000);
            poll();
          </script>
        </body>
        </html>
        """;

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }
}

/// <summary>
/// JSON-serializable status object returned by the /status endpoint.
/// </summary>
public class PresentationStatus
{
    public int CurrentIndex { get; set; }
    public int SlideCount { get; set; }
    public bool IsPlaying { get; set; }
    public string? CurrentUrl { get; set; }
    public double ZoomFactor { get; set; } = 1.0;
}
