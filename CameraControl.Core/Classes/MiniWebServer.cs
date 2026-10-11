using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace CameraControl.Core.Classes
{
    /// <summary>
    /// 极简 OAuth 回调监听器(Dropbox 等自动导出插件用)。
    /// 第三工程删掉了基于 HttpListener 的旧 MiniWebServer,这里用同样契约重写:
    /// new MiniWebServer(Func&lt;HttpListenerRequest,string&gt;, params string[] prefixes) + Run()/Stop()。
    /// </summary>
    public class MiniWebServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Func<HttpListenerRequest, string> _responderMethod;
        public bool IsRunning { get; private set; }

        public MiniWebServer(string[] prefixes, Func<HttpListenerRequest, string> method)
        {
            if (prefixes == null || prefixes.Length == 0)
                throw new ArgumentException("prefixes");
            if (method == null)
                throw new ArgumentException("method");
            foreach (string s in prefixes)
                _listener.Prefixes.Add(s);
            _responderMethod = method;
        }

        public MiniWebServer(Func<HttpListenerRequest, string> method, params string[] prefixes)
            : this(prefixes, method) { }

        public void Run()
        {
            IsRunning = true;
            _listener.Start();
            ThreadPool.QueueUserWorkItem(o =>
            {
                try
                {
                    while (_listener.IsListening)
                    {
                        HttpListenerContext ctx;
                        try { ctx = _listener.GetContext(); }
                        catch { break; }
                        ThreadPool.QueueUserWorkItem(c =>
                        {
                            var httpCtx = c as HttpListenerContext;
                            try
                            {
                                string rstr = _responderMethod(httpCtx.Request);
                                byte[] buf = Encoding.UTF8.GetBytes(rstr);
                                httpCtx.Response.ContentLength64 = buf.Length;
                                httpCtx.Response.OutputStream.Write(buf, 0, buf.Length);
                                if (rstr.Contains("OK"))
                                    Stop();
                            }
                            catch { }
                            finally
                            {
                                try { httpCtx.Response.OutputStream.Close(); } catch { }
                            }
                        }, ctx);
                    }
                }
                catch { }
            });
        }

        public void Stop()
        {
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch { }
            IsRunning = false;
        }
    }
}
