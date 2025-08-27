using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Xml;
using System.IO;

namespace GetACSEvent
{
    public static class DeviceConfigUpdater
    {
        public static void UpdateFromApi(string apiUrl, string configPath)
        {
            try
            {
                string json = HttpGet(apiUrl, 8000);
                if (string.IsNullOrEmpty(json)) return;

                // 将API原始内容保存到本地，便于排查与备用
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string rawPath = Path.Combine(baseDir, "ApiDevice_raw.json");
                    File.WriteAllText(rawPath, json, Encoding.UTF8);
                }
                catch { }

                // 朴素JSON解析：仅依赖简单结构，查找对象数组，筛选字段
                // 期望字段名：device_type, is_important, deviceid, areaid, ipaddress, device_name
                var items = ParseArrayOfObjects(json);
                var filtered = new List<Dictionary<string, string>>();
                foreach (var obj in items)
                {
                    string deviceType = (Get(obj, "device_type") ?? string.Empty).Trim();
                    string isImportant = (Get(obj, "is_important") ?? string.Empty).Trim();
                    if (string.Equals(deviceType, "门禁", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(isImportant, "否", StringComparison.OrdinalIgnoreCase))
                    {
                        filtered.Add(obj);
                    }
                }

                // 将筛选后的JSON保存
                try
                {
                    string baseDir2 = AppDomain.CurrentDomain.BaseDirectory;
                    string filteredPath = Path.Combine(baseDir2, "ApiDevice_filtered.json");
                    File.WriteAllText(filteredPath, SerializeArray(filtered), Encoding.UTF8);
                }
                catch { }

                MergeXmlByIp(filtered, configPath);
            }
            catch
            {
                // 忽略更新失败，不阻断启动
            }
        }

        private static string Get(Dictionary<string, string> obj, string key)
        {
            string v; return obj.TryGetValue(key, out v) ? v : string.Empty;
        }

        // 合并策略：以IP为关键字，更新/新增映射字段；未在API出现的本地项保持不动
        private static void MergeXmlByIp(List<Dictionary<string, string>> items, string path)
        {
            var doc = new XmlDocument();
            XmlElement root = null;
            if (File.Exists(path))
            {
                doc.Load(path);
                root = doc.SelectSingleNode("//Devices") as XmlElement;
            }
            if (root == null)
            {
                var decl = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
                doc.AppendChild(decl);
                root = doc.CreateElement("Devices");
                doc.AppendChild(root);
            }

            // 构建现有索引(IP -> Device节点)
            var index = new System.Collections.Generic.Dictionary<string, XmlElement>(StringComparer.OrdinalIgnoreCase);
            var nodes = doc.SelectNodes("//Devices/Device");
            if (nodes != null)
            {
                foreach (XmlNode n in nodes)
                {
                    var ipNode = n.SelectSingleNode("IP");
                    if (ipNode != null && !string.IsNullOrEmpty(ipNode.InnerText))
                    {
                        index[ipNode.InnerText.Trim()] = (XmlElement)n;
                    }
                }
            }

            foreach (var obj in items)
            {
                string deviceID = Get(obj, "deviceid");
                string areaID = Get(obj, "areaid");
                string ip = Get(obj, "ipaddress");
                string deviceName = Get(obj, "device_name");
                bool ipWasMissing = string.IsNullOrEmpty(ip);
                if (ipWasMissing)
                {
                    // 按模板创建
                    ip = "192.168.0.164";
                }

                XmlElement dev;
                if (!index.TryGetValue(ip.Trim(), out dev))
                {
                    dev = doc.CreateElement("Device");
                    root.AppendChild(dev);
                    SetOrCreate(doc, dev, "IP", ip);
                    // 为新项设置默认字段
                    if (ipWasMissing)
                    {
                        // 使用用户提供的模板默认值
                        SetOrCreate(doc, dev, "UserName", "admin");
                        SetOrCreate(doc, dev, "Password", "scyzkj123456");
                        SetOrCreate(doc, dev, "Port", "8000");
                        SetOrCreate(doc, dev, "Enabled", "true");
                        SetIfMissing(doc, dev, "Remark", "位于大厅前门的门禁设备");
                    }
                    else
                    {
                        // 保持之前的默认策略
                        SetOrCreate(doc, dev, "UserName", "admin");
                        SetOrCreate(doc, dev, "Password", "scyzkj123456");
                        SetOrCreate(doc, dev, "Port", "8000");
                        SetOrCreate(doc, dev, "Enabled", "true");
                        SetIfMissing(doc, dev, "Remark", "来自API");
                    }
                    index[ip.Trim()] = dev;
                }

                // 更新映射字段；保留其他本地字段不变
                if (ipWasMissing)
                {
                    // 使用模板默认值作为兜底
                    SetOrCreate(doc, dev, "DeviceID", string.IsNullOrEmpty(deviceID) ? "8f283fe3ca6947fdaba16db6ef3a7914" : deviceID);
                    SetOrCreate(doc, dev, "AreaID", string.IsNullOrEmpty(areaID) ? "root000000" : areaID);
                    string finalName = !string.IsNullOrEmpty(deviceName) ? deviceName : "前门门禁";
                    SetOrCreate(doc, dev, "DeviceName", finalName);
                    SetOrCreate(doc, dev, "Name", finalName);
                }
                else
                {
                    SetOrCreate(doc, dev, "DeviceID", deviceID);
                    SetOrCreate(doc, dev, "AreaID", areaID);
                    if (!string.IsNullOrEmpty(deviceName))
                    {
                        SetOrCreate(doc, dev, "DeviceName", deviceName);
                        SetOrCreate(doc, dev, "Name", deviceName);
                    }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            doc.Save(path);
        }

        private static void Append(XmlDocument doc, XmlElement parent, string name, string value)
        {
            var e = doc.CreateElement(name);
            e.InnerText = value ?? string.Empty;
            parent.AppendChild(e);
        }

        private static void SetOrCreate(XmlDocument doc, XmlElement parent, string name, string value)
        {
            if (value == null) value = string.Empty;
            var node = parent.SelectSingleNode(name) as XmlElement;
            if (node == null)
            {
                node = doc.CreateElement(name);
                parent.AppendChild(node);
            }
            node.InnerText = value;
        }

        private static void SetIfMissing(XmlDocument doc, XmlElement parent, string name, string defaultValue)
        {
            var node = parent.SelectSingleNode(name) as XmlElement;
            if (node == null)
            {
                node = doc.CreateElement(name);
                node.InnerText = defaultValue ?? string.Empty;
                parent.AppendChild(node);
            }
        }

        private static string HttpGet(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        // 极简JSON数组对象解析器，仅支持扁平字符串键值对，如 [{"k":"v",...}, ...]
        private static List<Dictionary<string, string>> ParseArrayOfObjects(string json)
        {
            var list = new List<Dictionary<string, string>>();
            if (string.IsNullOrEmpty(json)) return list;
            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '[') return list;
            i++;
            while (true)
            {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ']') { i++; break; }
                var obj = ParseObject(json, ref i);
                if (obj != null) list.Add(obj);
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ',') { i++; continue; }
                if (i < json.Length && json[i] == ']') { i++; break; }
                break;
            }
            return list;
        }

        private static Dictionary<string, string> ParseObject(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '{') return null;
            i++;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; break; }
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') break;
                i++;
                SkipWs(s, ref i);
                string val = null;
                if (i < s.Length && s[i] == '"')
                {
                    val = ParseString(s, ref i);
                }
                else if (i < s.Length && (s[i] == '{' || s[i] == '['))
                {
                    // 跳过复杂对象/数组值
                    SkipComplexValue(s, ref i);
                    val = string.Empty;
                }
                else
                {
                    val = ParseNonString(s, ref i);
                }
                dict[key] = val;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return dict;
        }

        private static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return string.Empty;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char esc = s[i++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 3 < s.Length)
                            {
                                string hex = s.Substring(i, 4);
                                ushort code;
                                if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
                                {
                                    sb.Append((char)code);
                                }
                                i += 4;
                            }
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string ParseNonString(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ',' || c == '}' || c == ']' || char.IsWhiteSpace(c)) break;
                i++;
            }
            return s.Substring(start, i - start).Trim(' ', '"');
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static void SkipComplexValue(string s, ref int i)
        {
            if (i >= s.Length) return;
            char open = s[i];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    // 跳过字符串中的内容
                    while (i < s.Length)
                    {
                        char c2 = s[i++];
                        if (c2 == '"') break;
                        if (c2 == '\\' && i < s.Length) i++; // 跳过转义
                    }
                    continue;
                }
                if (c == open) depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth <= 0) break;
                }
            }
        }

        private static string SerializeArray(List<Dictionary<string, string>> list)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('{');
                int j = 0;
                foreach (var kv in list[i])
                {
                    if (j++ > 0) sb.Append(',');
                    sb.Append('"').Append(Escape(kv.Key)).Append('"').Append(':');
                    sb.Append('"').Append(Escape(kv.Value)).Append('"');
                }
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
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

