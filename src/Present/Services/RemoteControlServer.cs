using System.Net;
using System.Text;
using System.Text.Json;

namespace Present.Services;

/// <summary>
/// Embedded HTTP server on port 9123 providing remote control of the presentation.
/// Endpoints: GET /next, /prev, /play, /stop, /zoomin, /zoomout, /scroll?dy=N, /status, /
/// </summary>
public class RemoteControlServer : IDisposable
{
    private HttpListener _listener;
    private readonly int _port;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private bool _disposed;
    private bool _started;

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
        _listener = CreateListener(port, bindAnyHost: true);
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RemoteControlServer));
        if (_started) return;

        _cts = new CancellationTokenSource();
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // Try localhost only as fallback.
            _listener.Close();
            _listener = CreateListener(_port, bindAnyHost: false);
            _listener.Start();
        }
        _started = true;
        _listenerTask = Task.Run(() => ListenLoop(_cts.Token));
    }

    public void Stop()
    {
        if (!_started) return;

        _cts?.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
            // Listener already disposed; stopping is complete.
        }
        finally
        {
            _started = false;
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequest(ctx), ct);
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;
        var path = req.Url?.AbsolutePath ?? "/";
        var query = req.Url?.Query ?? "";

        try
        {
            switch (path)
            {
                case "/":
                    ServeHtml(resp, GetControlPageHtml());
                    return;

                case "/next":
                    OnNext?.Invoke();
                    break;
                case "/prev":
                    OnPrev?.Invoke();
                    break;
                case "/play":
                    OnPlay?.Invoke();
                    break;
                case "/stop":
                    OnStop?.Invoke();
                    break;
                case "/zoomin":
                    OnZoomIn?.Invoke();
                    break;
                case "/zoomout":
                    OnZoomOut?.Invoke();
                    break;
                case "/scroll":
                    if (TryParseQueryParam(query, "dy", out var dy))
                        OnScroll?.Invoke(dy);
                    break;
                case "/status":
                    // Just fall through to JSON response
                    break;
                default:
                    resp.StatusCode = 404;
                    resp.Close();
                    return;
            }

            // Return JSON status
            var status = GetStatus?.Invoke() ?? new PresentationStatus();
            ServeJson(resp, status);
        }
        catch
        {
            try { resp.StatusCode = 500; resp.Close(); } catch { }
        }
    }

    private static bool TryParseQueryParam(string query, string param, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(query)) return false;
        var qs = query.TrimStart('?');
        foreach (var part in qs.Split('&'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0] == param && int.TryParse(kv[1], out int val))
            {
                value = val;
                return true;
            }
        }
        return false;
    }

    private static HttpListener CreateListener(int port, bool bindAnyHost)
    {
        var listener = new HttpListener();
        var hostPrefix = bindAnyHost ? "+" : "localhost";
        listener.Prefixes.Add($"http://{hostPrefix}:{port}/");
        return listener;
    }

    private static void ServeHtml(HttpListenerResponse resp, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        resp.ContentType = "text/html; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        resp.OutputStream.Write(bytes);
        resp.Close();
    }

    private static void ServeJson(HttpListenerResponse resp, object obj)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.ContentType = "application/json; charset=utf-8";
        resp.Headers.Add("Access-Control-Allow-Origin", "*");
        resp.ContentLength64 = bytes.Length;
        resp.OutputStream.Write(bytes);
        resp.Close();
    }

    private static string GetControlPageHtml() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">
          <title>Present Remote</title>
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
          <h1>Present Remote</h1>
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
            _listener.Close();
            _cts?.Dispose();
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
