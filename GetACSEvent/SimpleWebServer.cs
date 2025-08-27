using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
// 不使用 System.Web.Script.Serialization，避免 System.Web.Extensions 依赖

namespace GetACSEvent
{
    public class SimpleWebServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly EventStore _store;
        private Thread _thread;
        private bool _running;

        public SimpleWebServer(string prefix, EventStore store)
        {
            _store = store;
            _listener.Prefixes.Add(prefix);
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _listener.Start();
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            try { _listener.Stop(); } catch { }
        }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    Handle(ctx);
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
            if (path == "/config/edit" && ctx.Request.HttpMethod == "GET")
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DeviceConfig.xml");
                string xml = "";
                try { if (File.Exists(configPath)) xml = File.ReadAllText(configPath, Encoding.UTF8); } catch { }
                bool saved = string.Equals(ctx.Request.QueryString["saved"], "1", StringComparison.OrdinalIgnoreCase);
                string page = BuildEditorPage(xml, saved);
                Respond(ctx, 200, "text/html; charset=utf-8", page);
                return;
            }
            if (path == "/events")
            {
                var events = _store.Snapshot();
                var json = SerializeEvents(events);
                Respond(ctx, 200, "application/json", json);
                return;
            }
            if (path == "/config" && ctx.Request.HttpMethod == "GET")
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DeviceConfig.xml");
                if (!File.Exists(configPath))
                {
                    Respond(ctx, 404, "text/plain", "DeviceConfig.xml not found");
                    return;
                }
                string xml = File.ReadAllText(configPath, Encoding.UTF8);
                Respond(ctx, 200, "application/xml", xml);
                return;
            }
            if (path == "/config" && ctx.Request.HttpMethod == "POST")
            {
                using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();
                    string contentType = ctx.Request.ContentType ?? string.Empty;
                    string xmlPayload = body;
                    if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                    {
                        // 解析表单字段 xml=...
                        var form = ParseForm(body);
                        form.TryGetValue("xml", out xmlPayload);
                    }
                    // 简单校验是有效XML
                    try
                    {
                        var doc = new XmlDocument();
                        doc.LoadXml(xmlPayload ?? string.Empty);
                        string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DeviceConfig.xml");
                        File.WriteAllText(configPath, xmlPayload ?? string.Empty, Encoding.UTF8);
                        // 如果从表单提交，返回重定向到编辑页
                        if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                        {
                            ctx.Response.StatusCode = 302;
                            ctx.Response.RedirectLocation = "/config/edit?saved=1";
                            ctx.Response.OutputStream.Close();
                            return;
                        }
                        Respond(ctx, 200, "text/plain", "OK");
                    }
                    catch (Exception ex)
                    {
                        Respond(ctx, 400, "text/plain", "Invalid XML: " + ex.Message);
                    }
                }
                return;
            }
            // 简单首页
            Respond(ctx, 200, "text/html; charset=utf-8", "<html><body><h3>GetACSEvent Web</h3><ul><li><a href=\"/events\">/events</a></li><li><a href=\"/config\">/config</a> (GET)</li><li><a href=\"/config/edit\">/config/edit</a> (表单编辑)</li></ul></body></html>");
        }

        private void Respond(HttpListenerContext ctx, int status, string contentType, string content)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = contentType;
            var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.ContentLength64 = bytes.Length;
            using (var s = ctx.Response.OutputStream)
            {
                s.Write(bytes, 0, bytes.Length);
            }
        }

        private string BuildEditorPage(string xml, bool saved)
        {
            string escaped = System.Security.SecurityElement.Escape(xml ?? string.Empty) ?? string.Empty;
            var sb = new StringBuilder();
            sb.Append("<html><head><meta charset=\"utf-8\"><title>编辑配置</title>");
            sb.Append("<style>textarea{width:100%;height:70vh;font-family:Consolas,monospace;font-size:12px;} body{max-width:1000px;margin:20px auto;padding:0 10px;} .bar{margin:10px 0}</style>");
            sb.Append("</head><body><h3>编辑 DeviceConfig.xml</h3>");
            if (saved)
            {
                sb.Append("<div style=\"padding:8px 12px;background:#e6ffed;border:1px solid #b7eb8f;color:#389e0d;margin-bottom:10px;\">保存成功</div>");
            }
            sb.Append("<div class=\"bar\"><a href=\"/config\" target=\"_blank\">查看XML</a> | <a href=\"/\">首页</a></div>");
            sb.Append("<form method=\"post\" action=\"/config\">\n");
            sb.Append("<textarea name=\"xml\">").Append(escaped).Append("</textarea>\n");
            sb.Append("<div class=\"bar\"><button type=\"submit\">保存</button></div>\n");
            sb.Append("</form></body></html>");
            return sb.ToString();
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseForm(string body)
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(body)) return dict;
            var parts = body.Split('&');
            foreach (var part in parts)
            {
                var kv = part.Split(new char[]{'='}, 2);
                string rawK = kv[0] ?? string.Empty;
                string rawV = kv.Length > 1 ? kv[1] : string.Empty;
                string k = UrlDecodeFormComponent(rawK);
                string v = UrlDecodeFormComponent(rawV);
                dict[k] = v;
            }
            return dict;
        }

        private static string UrlDecodeFormComponent(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // application/x-www-form-urlencoded 将空格编码为'+'
            s = s.Replace('+', ' ');
            try { return Uri.UnescapeDataString(s); } catch { return s; }
        }

        private static string SerializeEvents(System.Collections.Generic.List<AcsEvent> list)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                AppendJson(sb, "timeUtc", e.TimeUtc.ToString("o")); sb.Append(',');
                AppendJson(sb, "deviceIP", e.DeviceIP); sb.Append(',');
                AppendJson(sb, "deviceName", e.DeviceName); sb.Append(',');
                AppendJson(sb, "deviceID", e.DeviceID); sb.Append(',');
                AppendJson(sb, "areaID", e.AreaID); sb.Append(',');
                AppendJson(sb, "remark", e.Remark); sb.Append(',');
                AppendJson(sb, "majorType", e.MajorType); sb.Append(',');
                AppendJson(sb, "minorType", e.MinorType); sb.Append(',');
                AppendJson(sb, "cardNo", e.CardNo); sb.Append(',');
                AppendJson(sb, "employeeNo", e.EmployeeNo); sb.Append(',');
                AppendJson(sb, "personName", e.PersonName); sb.Append(',');
                AppendJson(sb, "cardType", e.CardType); sb.Append(',');
                sb.Append("\"doorNo\":").Append(e.DoorNo);
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static void AppendJson(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(Escape(key)).Append('"').Append(':');
            if (value == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append('"').Append(Escape(value)).Append('"');
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 32)
                        {
                            sb.AppendFormat("\\u{0:X4}", (int)ch);
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

