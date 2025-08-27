using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;
using System.Net;
using System.Xml;
using System.Net.NetworkInformation;

namespace GetACSEvent
{
    /// <summary>
    /// 门禁事件监控控制台程序
    /// </summary>
    class ACSEventConsole
    {
        // 员工ID到姓名的哈希表
        public static Dictionary<string, string> EmployeeNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("多门禁事件监控服务启动中...");
                Console.WriteLine("配置文件: DeviceConfig.xml");

                // 启动前：从API拉取并更新配置
                try
                {
                    string apiUrl = "http://192.168.0.14:5000/api/device";
                    string configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DeviceConfig.xml");
                    DeviceConfigUpdater.UpdateFromApi(apiUrl, configPath);
                    Console.WriteLine("已从API更新配置: " + apiUrl);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("从API更新配置失败: " + ex.Message);
                }

                // 启动前：从API拉取并保存员工配置
                try
                {
                    string employeeApiUrl = "http://192.168.0.14:5000/api/employee";
                    string employeeConfigPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EmployeeConfig.json");
                    SaveEmployeeConfig(employeeApiUrl, employeeConfigPath);
                    Console.WriteLine("已从API保存员工配置: " + employeeApiUrl);
                    Console.WriteLine($"已加载 {EmployeeNameMap.Count} 个员工信息到哈希表");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("从API保存员工配置失败: " + ex.Message);
                }
                
                // 创建并启动多门禁事件服务
                ACSEventMultiDeviceService service = new ACSEventMultiDeviceService("DeviceConfig.xml");
                service.Start();

                // 启动内置Web服务，优先绑定到本机，不使用 http://+/ 以避免URL ACL权限问题
                SimpleWebServer web = null;
                string[] prefixes = new string[] {
                    "http://+:8080/",     
                    "http://localhost:8080/",
                    "http://127.0.0.1:8080/",
                    "http://localhost:8081/",
                    "http://127.0.0.1:8081/"
                };
                bool webStarted = false;
                string startedPrefix = "";
                foreach (var p in prefixes)
                {
                    try
                    {
                        web = new SimpleWebServer(p, ACSEventMultiDeviceService.SharedEventStore);
                        web.Start();
                        startedPrefix = p;
                        webStarted = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Web服务启动失败于前缀 " + p + " : " + ex.Message);
                    }
                }
                
                if (webStarted)
                {
                    // 获取本机IP地址并显示
                    string localIP = GetLocalIPAddress();
                    if (!string.IsNullOrEmpty(localIP))
                    {
                        Console.WriteLine($"Web服务已启动: http://{localIP}:8080/");
                    }
                    else
                    {
                        Console.WriteLine("Web服务已启动: " + startedPrefix);
                    }
                }
                else
                {
                    Console.WriteLine("提示: 你也可以以管理员身份运行，或执行如下命令开放URL ACL权限：");
                    Console.WriteLine("  netsh http add urlacl url=http://+:8080/ user=Everyone");
                }

                Console.WriteLine("按ESC键退出...");
                
                // 等待ESC键退出
                while (true)
                {
                    if (Console.KeyAvailable)
                    {
                        ConsoleKeyInfo key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Escape)
                        {
                            break;
                        }
                    }
                    System.Threading.Thread.Sleep(100);
                }

                // 停止服务
                try { if (webStarted && web != null) web.Stop(); } catch { }
                service.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine("程序发生异常：" + ex.Message);
                Console.WriteLine("异常详细信息：" + ex.ToString());
                Console.WriteLine("按任意键退出...");
                Console.ReadKey();
            }
        }

        /// <summary>
        /// 获取本机IP地址
        /// </summary>
        /// <returns>本机IP地址</returns>
        private static string GetLocalIPAddress()
        {
            try
            {
                // 获取所有网络接口
                NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                
                foreach (NetworkInterface networkInterface in networkInterfaces)
                {
                    // 只获取活动的以太网或无线网络接口
                    if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                        (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                         networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    {
                        IPInterfaceProperties ipProperties = networkInterface.GetIPProperties();
                        foreach (UnicastIPAddressInformation ip in ipProperties.UnicastAddresses)
                        {
                            // 只获取IPv4地址，排除回环地址
                            if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                !IPAddress.IsLoopback(ip.Address))
                            {
                                return ip.Address.ToString();
                            }
                        }
                    }
                }
                
                // 如果没有找到合适的IP，返回localhost
                return "localhost";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取本机IP地址失败: {ex.Message}");
                return "localhost";
            }
        }

        /// <summary>
        /// 保存员工配置到本地文件并解析为哈希表
        /// </summary>
        /// <param name="apiUrl">员工API地址</param>
        /// <param name="configPath">本地保存路径</param>
        private static void SaveEmployeeConfig(string apiUrl, string configPath)
        {
            try
            {
                // 从API获取员工数据
                string json = HttpGet(apiUrl, 8000);
                if (string.IsNullOrEmpty(json)) return;

                // 添加调试信息：显示API响应的前500个字符
                // Console.WriteLine($"[调试] API响应内容预览（前500字符）:");
                // string preview = json.Length > 500 ? json.Substring(0, 500) + "..." : json;
                // Console.WriteLine(preview);

                // 保存原始JSON到本地
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                File.WriteAllText(configPath, json, Encoding.UTF8);

                // 解析JSON并提取employeeId和name到哈希表
                ParseEmployeeData(json);
            }
            catch (Exception ex)
            {
                throw new Exception($"保存员工配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析员工JSON数据，提取employeeId和name到哈希表
        /// </summary>
        /// <param name="json">员工数据JSON字符串</param>
        private static void ParseEmployeeData(string json)
        {
            try
            {
                // 清空现有哈希表
                EmployeeNameMap.Clear();
                // Console.WriteLine($"[调试] 开始解析员工数据，JSON长度: {json?.Length ?? 0}");

                // 使用简单的JSON解析方法（类似DeviceConfigUpdater中的方法）
                var employees = ParseArrayOfObjects(json);
                // Console.WriteLine($"[调试] 解析到 {employees.Count} 个员工对象");
                
                foreach (var employee in employees)
                {
                    string employeeId = Get(employee, "employeeId");
                    string name = Get(employee, "name");
                    
                    // Console.WriteLine($"[调试] 解析员工: employeeId='{employeeId}', name='{name}'");
                    
                    if (!string.IsNullOrEmpty(employeeId) && !string.IsNullOrEmpty(name))
                    {
                        string trimmedEmployeeId = employeeId.Trim();
                        string trimmedName = name.Trim();
                        EmployeeNameMap[trimmedEmployeeId] = trimmedName;
                        // Console.WriteLine($"[调试] 添加到哈希表: '{trimmedEmployeeId}' -> '{trimmedName}'");
                    }
                    else
                    {
                        // Console.WriteLine($"[调试] 跳过无效员工数据: employeeId='{employeeId}', name='{name}'");
                    }
                }
                
                // Console.WriteLine($"[调试] 哈希表填充完成，总数: {EmployeeNameMap.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"解析员工数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从字典中获取值
        /// </summary>
        private static string Get(Dictionary<string, string> obj, string key)
        {
            string v; return obj.TryGetValue(key, out v) ? v : string.Empty;
        }

        /// <summary>
        /// 极简JSON数组对象解析器，仅支持扁平字符串键值对，如 [{"k":"v",...}, ...]
        /// </summary>
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

        /// <summary>
        /// 解析JSON对象
        /// </summary>
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

        /// <summary>
        /// 解析JSON字符串
        /// </summary>
        private static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return string.Empty;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    switch (s[i])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(s[i]); break;
                    }
                }
                else
                {
                    sb.Append(s[i]);
                }
                i++;
            }
            if (i < s.Length) i++;
            return sb.ToString();
        }

        /// <summary>
        /// 解析非字符串值
        /// </summary>
        private static string ParseNonString(string s, ref int i)
        {
            var sb = new StringBuilder();
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ',' && s[i] != '}' && s[i] != ']')
            {
                sb.Append(s[i]);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 跳过空白字符
        /// </summary>
        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        /// <summary>
        /// 跳过复杂值（对象或数组）
        /// </summary>
        private static void SkipComplexValue(string s, ref int i)
        {
            int depth = 0;
            char startChar = s[i];
            char endChar = (startChar == '{') ? '}' : ']';
            i++;
            while (i < s.Length)
            {
                if (s[i] == startChar) depth++;
                else if (s[i] == endChar)
                {
                    if (depth == 0) { i++; break; }
                    depth--;
                }
                i++;
            }
        }

        /// <summary>
        /// HTTP GET请求
        /// </summary>
        /// <param name="url">请求URL</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <returns>响应内容</returns>
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
    }
}