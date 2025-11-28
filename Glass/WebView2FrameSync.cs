using Microsoft.Web.WebView2.WinForms;
using SkiaSharp;
using Svg.Skia;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace Glass;

public sealed class WebView2FrameSync : IDisposable
{
    private readonly WebView2 _webView;
    private readonly Form _form;
    private readonly HttpClient _http = new();
    private readonly NotifyIcon _tray;
    private string lastIconHref = string.Empty;
    private string lastTitle = string.Empty;
    private bool disposed;

    public WebView2FrameSync(WebView2 webView, Form form)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _form = form ?? throw new ArgumentNullException(nameof(form));

        _tray = new NotifyIcon
        {
            Visible = false,
            Text = form.Text
        };
        
        _tray.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _form.Activate();
        };

        _tray.MouseDoubleClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _form.Activate();
        };
    }

    public Boolean ShowSysTrayIcon
    {
        get
        {
            return this._tray.Visible;
        }

        set
        {
            this._tray.Visible = value;
        }
    }

    /// <summary>Start automatically updating the form icon on each navigation.</summary>
    public void Enable()
    {
        _webView.NavigationCompleted += async (_, e) =>
        {
            if (!e.IsSuccess) return;
            await ApplyIconAsync();
        };
    }

    public void Update()
    {
        this._form.Invoke(async () =>
        {
            await ApplyIconAsync();
        });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        _tray.Visible = false;
        _tray.Dispose();
        _http.Dispose();
    }

    private void UpdateFormAndTaskbarIcon(Icon icon)
    {
        _form.Icon = icon;
        _tray.Icon = icon;
    }

    private async Task ApplyIconAsync()
    {
        try
        {
            var htmlJson = await _webView.CoreWebView2
                .ExecuteScriptAsync("document.documentElement.outerHTML")
                .ConfigureAwait(false);

            var html = System.Text.Json.JsonSerializer.Deserialize<string>(htmlJson);

            if (string.IsNullOrWhiteSpace(html)) return;

            var pageInfo = ExtractIconUrlAndTitle(html);

            Uri pageUri = _webView.Source;

            string iconHref;

            if (!string.IsNullOrWhiteSpace(pageInfo.iconUrl))
            {
                iconHref = pageInfo.iconUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    ? pageInfo.iconUrl
                    : NormalizeUrl(pageInfo.iconUrl, pageUri);
            }
            else
            {
                iconHref = $"{pageUri.Scheme}://{pageUri.Host}/favicon.ico";
            }

            string pageTitle;

            if (!string.IsNullOrWhiteSpace(pageInfo.title))
            {
                pageTitle = pageInfo.title;
            } 
            else
            {
                pageTitle = string.Empty;
            }

            if (lastIconHref != iconHref)
            {
                await LoadAndApplyIcon(iconHref);

                lastIconHref = iconHref;
            }

            if (lastTitle != pageTitle)
            {
                this._form.Invoke(() =>
                {
                    this._form.Text = pageTitle;
                    this._tray.Text = pageTitle;
                });

                lastTitle = pageTitle;
            }
        }
        catch
        {
            // ignore
        }
    }

    private async Task LoadAndApplyIcon(string href)
    {
        try
        {
            var icon = await LoadIconFromUriAsync(href);

            if (icon != null)
            {
                this._form.Invoke(() =>
                {
                    this.UpdateFormAndTaskbarIcon(icon);
                });
            }                
        }
        catch
        {
            // ignore
        }
    }

    private async Task<Icon?> LoadIconFromUriAsync(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;

        byte[]? data = null;

        if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = href.IndexOf(',');
            if (commaIndex < 0) return null;

            var metadata = href.Substring(5, commaIndex - 5); // e.g., image/png;base64
            var dataPart = href.Substring(commaIndex + 1);

            if (!metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                return null;

            data = Convert.FromBase64String(dataPart);

            if (metadata.Contains("svg", StringComparison.OrdinalIgnoreCase))
                return CreateIconFromSvg(data);
        }
        else if (href.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var localPath = Uri.UnescapeDataString(new Uri(href).LocalPath);
            if (!File.Exists(localPath)) return null;

            data = File.ReadAllBytes(localPath);

            if (localPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                return CreateIconFromSvg(data);
        }
        else
        {
            data = await _http.GetByteArrayAsync(href);
            if (href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                return CreateIconFromSvg(data);
        }

        if (data != null)
        {
            using var ms = new MemoryStream(data);
            return CreateIconFromStream(ms);
        }

        return null;
    }

    private Icon? CreateIconFromStream(Stream ms)
    {
        try
        {
            return new Icon(ms);
        }
        catch
        {
            ms.Position = 0;
            using var bmp = new Bitmap(ms);
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    private Icon? CreateIconFromSvg(byte[] svgBytes, int size = 32)
    {
        try
        {
            using var stream = new MemoryStream(svgBytes);
            var svg = new SKSvg();
            svg.Load(stream);

            var bmp = new SKBitmap(size, size);
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(SKColors.Transparent);

            var scaleX = size / svg.Picture.CullRect.Width;
            var scaleY = size / svg.Picture.CullRect.Height;
            var scale = Math.Min(scaleX, scaleY);
            canvas.Scale((float)scale);
            canvas.DrawPicture(svg.Picture);
            canvas.Flush();

            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var msPng = data.AsStream();

            using var finalBmp = new Bitmap(msPng);
            return Icon.FromHandle(finalBmp.GetHicon());
        }
        catch
        {
            return null;
        }
    }

    private (string? iconUrl, string? title) ExtractIconUrlAndTitle(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var returnValue = (iconUrl: string.Empty, title: string.Empty);

        var nodes = doc.DocumentNode.SelectNodes("//link[@rel]");

        if (nodes != null)
        {
            foreach (var node in nodes)
            {
                var rel = node.GetAttributeValue("rel", "").ToLowerInvariant();
                if (rel == "icon" || rel == "shortcut icon")
                {
                    var href = node.GetAttributeValue("href", null);

                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        returnValue.iconUrl = href;
                    }
                }
            }

        }

        nodes = doc.DocumentNode.SelectNodes("//title");

        if (nodes != null)
        {
            foreach (var node in nodes)
            {
                var innerText = node.InnerText;

                if (!string.IsNullOrWhiteSpace(innerText))
                {
                    returnValue.title = innerText;
                }
            }

        }

        return returnValue;
    }

    private string NormalizeUrl(string href, Uri baseUri)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var abs))
            return abs.ToString();

        return new Uri(baseUri, href).ToString();
    }
}
