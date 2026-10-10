using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CameraControl.Core;
using CameraControl.Core.Scripting;
using System.Linq;
using CameraControl.Core.Classes;
using CameraControl.Devices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CameraControl.Core.Web
{
    /// <summary>
    /// 第三工程:取代 Griffin.WebServer(WebServer.cs/WebServerModule.cs/WebServerOld.cs/MiniWebServer.cs)。
    /// ASP.NET Core minimal API + WebSocket,面向手机浏览器遥控:
    ///   GET /                    -> 静态遥控页(WebServerFolder,即 CameraControl.Application/WebServer 内容)
    ///   GET /liveview.jpg        -> 当前 LiveView 帧(MJPEG 轮询用)
    ///   GET /liveview.mjpeg      -> multipart/x-mixed-replace 连续流
    ///   GET /session.json        -> 会话文件列表
    ///   GET /settings.json       -> 设置快照
    ///   GET /filelist.json       -> FileListItem 列表
    ///   GET /thumb/large/{name}  -> 大缩略图
    ///   GET /thumb/small/{name}  -> 小缩略图
    ///   GET /image/{name}        -> 原图
    ///   GET /cmd?slc=..&camera=..&param1=..  -> 命令行处理器(需 AllowWebserverActions)
    ///   WS   /ws                 -> 事件推送(拍照完成通知等,JSON 行协议)
    /// </summary>
    public class ModernWebServer
    {
        private Microsoft.AspNetCore.Builder.WebApplication _app;

        public void Start(int port)
        {
            try
            {
                var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(
                    new WebApplicationOptions { ContentRootPath = AppDomain.CurrentDomain.BaseDirectory });
                builder.Logging.ClearProviders();
                builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
                _app = builder.Build();

                _app.UseWebSockets();

                var folder = Settings.WebServerFolder;
                if (Directory.Exists(folder))
                {
                    var provider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(folder));
                    _app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
                    _app.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = provider,
                        RequestPath = ""
                    });
                }

                MapRoutes(_app);

                _app.StartAsync().GetAwaiter().GetResult();
                Log.Info("ModernWebServer started on port " + port);
            }
            catch (Exception ex)
            {
                Log.Error("Unable to start modern web server", ex);
            }
        }

        public void Stop()
        {
            try { _app?.StopAsync().Wait(2000); } catch { }
        }

        internal static void MapRoutes(Microsoft.AspNetCore.Builder.WebApplication app)
        {
            app.MapGet("/liveview.jpg", async (HttpContext ctx) =>
            {
                var data = GetLiveView();
                if (data == null) { ctx.Response.StatusCode = 404; return; }
                ctx.Response.ContentType = "image/jpeg";
                await ctx.Response.Body.WriteAsync(data, 0, data.Length);
            });

            app.MapGet("/liveview.mjpeg", async (HttpContext ctx) =>
            {
                ctx.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
                while (!ctx.RequestAborted.IsCancellationRequested)
                {
                    var data = GetLiveView();
                    if (data != null)
                    {
                        await ctx.Response.WriteAsync("--frame\r\nContent-Type: image/jpeg\r\nContent-Length: " + data.Length + "\r\n\r\n");
                        await ctx.Response.Body.WriteAsync(data, 0, data.Length);
                        await ctx.Response.WriteAsync("\r\n");
                        await ctx.Response.Body.FlushAsync();
                    }
                    await Task.Delay(100);
                }
            });

            app.MapGet("/session.json", async (HttpContext ctx) =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonConvert.SerializeObject(ServiceProvider.Settings.DefaultSession, Formatting.Indented));
            });

            app.MapGet("/settings.json", async (HttpContext ctx) =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonConvert.SerializeObject(ServiceProvider.Settings, Formatting.Indented));
            });

            app.MapGet("/filelist.json", async (HttpContext ctx) =>
            {
                var items = ServiceProvider.Settings.DefaultSession.Files.Cast<FileItem>().Select(item => new FileListItem
                {
                    FileName = item.FileName,
                    LargeThumb = "/thumb/large/" + Path.GetFileName(item.LargeThumb),
                    SmallThumb = "/thumb/small/" + Path.GetFileName(item.SmallThumb),
                    Original = "/image/" + Path.GetFileName(item.FileName),
                    Name = item.Name,
                    Width = item.FileInfo.Width > 0 ? item.FileInfo.Width : 3000,
                    Height = item.FileInfo.Height > 0 ? item.FileInfo.Height : 2000,
                }).ToList();
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonConvert.SerializeObject(items, Formatting.Indented));
            });

            app.MapGet("/thumb/large/{name}", async (HttpContext ctx, string name) =>
                await ServeThumb(ctx, name, large: true));
            app.MapGet("/thumb/small/{name}", async (HttpContext ctx, string name) =>
                await ServeThumb(ctx, name, large: false));

            app.MapGet("/preview.jpg", async (HttpContext ctx) =>
            {
                var bitmap = ServiceProvider.Settings.SelectedBitmap;
                if (bitmap?.FileItem?.LargeThumb == null || !File.Exists(bitmap.FileItem.LargeThumb))
                {
                    ctx.Response.StatusCode = 404; return;
                }
                ctx.Response.ContentType = "image/jpeg";
                await ctx.Response.SendFileAsync(bitmap.FileItem.LargeThumb);
            });

            app.MapGet("/image/{name}", async (HttpContext ctx, string name) =>
            {
                var file = FindSessionFile(name);
                if (file == null || !File.Exists(file)) { ctx.Response.StatusCode = 404; return; }
                ctx.Response.ContentType = "application/octet-stream";
                await ctx.Response.SendFileAsync(file);
            });

            app.MapGet("/cmd", async (HttpContext ctx) =>
            {
                var q = ctx.Request.Query;
                var slc = q["slc"].ToString();
                ctx.Response.ContentType = "text/html";
                if (!ServiceProvider.Settings.AllowWebserverActions || string.IsNullOrEmpty(slc))
                {
                    await ctx.Response.WriteAsync(string.IsNullOrEmpty(slc) ? "OK" : "Forbidden");
                    return;
                }
                try
                {
                    var processor = new CommandLineProcessor();
                    processor.SetCamera(q["camera"].ToString());
                    var resp = processor.Pharse(new[] { slc, q["param1"].ToString(), q["param2"].ToString() });
                    var list = resp as System.Collections.Generic.IEnumerable<string>;
                    string response;
                    if (list != null)
                        response = string.Join("\n", list);
                    else
                        response = resp?.ToString() ?? "";
                    if (string.IsNullOrEmpty(response)) response = "OK";
                    await ctx.Response.WriteAsync(response);
                }
                catch (Exception ex)
                {
                    await ctx.Response.WriteAsync(ex.Message);
                }
            });

            app.Map("/ws", async (HttpContext ctx) =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
                var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                var buffer = new byte[4096];
                while (ctx.RequestAborted.IsCancellationRequested && ws.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var ping = Encoding.UTF8.GetBytes("{\"t\":\"ping\",\"utc\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}");
                    await ws.SendAsync(new ArraySegment<byte>(ping), System.Net.WebSockets.WebSocketMessageType.Text, true, ctx.RequestAborted);
                    var seg = new ArraySegment<byte>(buffer);
                    var res = await ws.ReceiveAsync(seg, ctx.RequestAborted);
                    if (res.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                }
            });
        }

        private static byte[] GetLiveView()
        {
            var dm = ServiceProvider.DeviceManager;
            if (dm?.SelectedCameraDevice == null) return null;
            if (dm.LiveViewImage == null || !dm.LiveViewImage.ContainsKey(dm.SelectedCameraDevice)) return null;
            var data = dm.LiveViewImage[dm.SelectedCameraDevice];
            return data == null || data.Length == 0 ? null : data;
        }

        private static async Task ServeThumb(HttpContext ctx, string name, bool large)
        {
            string path = null;
            foreach (FileItem item in ServiceProvider.Settings.DefaultSession.Files)
            {
                var candidate = large ? item.LargeThumb : item.SmallThumb;
                if (candidate != null &&
                    (Path.GetFileName(candidate) == name || item.Name == name))
                {
                    path = candidate;
                    break;
                }
            }
            if (path == null || !File.Exists(path))
                path = Path.Combine(Settings.WebServerFolder, "logo.png");
            if (!File.Exists(path)) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.ContentType = "image/jpeg";
            await ctx.Response.SendFileAsync(path);
        }

        private static string FindSessionFile(string name)
        {
            foreach (FileItem item in ServiceProvider.Settings.DefaultSession.Files)
            {
                if (Path.GetFileName(item.FileName) == name)
                    return item.FileName;
            }
            return null;
        }
    }
}
