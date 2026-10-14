using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StillMark.Services;

/// <summary>
/// Test-only automation channel. Inactive unless the app is started with "--automation &lt;pipe-name&gt;".
/// Uses a current-user-only named pipe; each request is one JSON line, each reply one JSON line.
/// Commands are executed on the UI thread through the same handlers the UI uses.
/// </summary>
public sealed class AutomationServer
{
    private readonly string _pipeName;
    private readonly Func<JsonObject, Task<JsonNode?>> _handler;

    public AutomationServer(string pipeName, Func<JsonObject, Task<JsonNode?>> handler)
    {
        _pipeName = pipeName;
        _handler = handler;
    }

    public void Start() => new Thread(Loop) { IsBackground = true, Name = "automation" }.Start();

    private void Loop()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                server.WaitForConnection();
                using var reader = new StreamReader(server, new UTF8Encoding(false));
                using var writer = new StreamWriter(server, new UTF8Encoding(false)) { AutoFlush = true };
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    JsonNode? result;
                    try
                    {
                        var req = JsonNode.Parse(line)!.AsObject();
                        var app = Application.Current;
                        // InvokeAsync keeps working while a modal dialog runs its nested message loop.
                        var op = app.Dispatcher.InvokeAsync(() => _handler(req));
                        var task = op.Task.Result;
                        result = task.Wait(TimeSpan.FromSeconds(30)) ? task.Result : JsonValue.Create("timeout");
                        result = new JsonObject { ["ok"] = true, ["result"] = result };
                    }
                    catch (Exception ex)
                    {
                        result = new JsonObject { ["ok"] = false, ["error"] = ex.GetBaseException().Message };
                    }
                    writer.WriteLine(result!.ToJsonString());
                }
            }
            catch
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>Renders a window (WPF visuals) and pastes an optional WebView2 capture over its host area.</summary>
    public static void SaveSnapshot(Window w, string path, FrameworkElement? webHost = null, byte[]? webPng = null)
    {
        var dpi = VisualTreeHelper.GetDpi(w);
        var root = (FrameworkElement)w.Content;
        int pw = (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), ph = (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY);
        // Render the live visual tree directly (no VisualBrush: it keeps layout callbacks alive), then composite.
        var wpf = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        wpf.Render(root);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(w.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawImage(wpf, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            if (webHost != null && webPng != null && webHost.IsVisible)
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = new MemoryStream(webPng);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                var pos = webHost.TranslatePoint(new Point(0, 0), root);
                dc.DrawImage(img, new Rect(pos, new Size(webHost.ActualWidth, webHost.ActualHeight)));
            }
        }
        var rtb = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
