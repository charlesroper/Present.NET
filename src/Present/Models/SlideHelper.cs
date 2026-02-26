namespace Present.Models;

/// <summary>
/// Utility methods for slide URL handling.
/// </summary>
public static class SlideHelper
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".gif", ".jpg", ".jpeg", ".webp", ".svg"
    };

    /// <summary>
    /// Returns true if the URL points to an image file.
    /// </summary>
    public static bool IsImageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var dot = path.LastIndexOf('.');
            if (dot < 0) return false;
            return ImageExtensions.Contains(path[dot..]);
        }
        catch
        {
            // Fallback: simple extension check
            var dot = url.LastIndexOf('.');
            if (dot < 0) return false;
            var ext = url[dot..].Split('?', '#')[0];
            return ImageExtensions.Contains(ext);
        }
    }

    /// <summary>
    /// Returns an HTML page that displays the image full-window on a black background.
    /// </summary>
    public static string GetImageHtml(string url)
    {
        var safeUrl = System.Net.WebUtility.HtmlEncode(url);
        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
            * { margin: 0; padding: 0; box-sizing: border-box; }
            html, body {
              width: 100vw; height: 100vh;
              background: #000;
              display: flex; align-items: center; justify-content: center;
              overflow: hidden;
            }
            img {
              max-width: 100%; max-height: 100%;
              object-fit: contain;
              display: block;
            }
            </style>
            </head>
            <body>
              <img src="{{safeUrl}}" alt="Slide"/>
            </body>
            </html>
            """;
    }
}
