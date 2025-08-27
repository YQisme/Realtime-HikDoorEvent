using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Xml;
using System.Threading;

namespace GetACSEvent
{
    /// <summary>
    /// 门禁设备配置信息
    /// </summary>
    public class DeviceInfo
    {
        public string IP { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public ushort Port { get; set; }
        public bool Enabled { get; set; }
        public string Name { get; set; }
        public string Remark { get; set; }
        public string DeviceID { get; set; }
        public string AreaID { get; set; }
        public string DeviceName { get; set; }

        public DeviceInfo()
        {
            IP = string.Empty;
            UserName = string.Empty;
            Password = string.Empty;
            Port = 8000;
            Enabled = true;
            Name = string.Empty;
            Remark = string.Empty;
            DeviceID = string.Empty;
            AreaID = string.Empty;
            DeviceName = string.Empty;
        }
    }

    /// <summary>
    /// 多门禁设备事件监控服务
    /// </summary>
    public class ACSEventMultiDeviceService
    {
        private List<DeviceInfo> m_DeviceList = new List<DeviceInfo>();
        private List<ACSEventService> m_ServiceList = new List<ACSEventService>();
        public static EventStore SharedEventStore = new EventStore(1000);
        private bool m_bRunning = false;
        private string m_ConfigFile = "DeviceConfig.xml";
        private bool m_bInitialized = false;
        private CHCNetSDK.MSGCallBack m_MsgCallback = null; // 全局报警回调函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="configFile">配置文件路径，默认为DeviceConfig.xml</param>
        public ACSEventMultiDeviceService(string configFile = "DeviceConfig.xml")
        {
            m_ConfigFile = configFile;
        }

        /// <summary>
        /// 加载设备配置
        /// </summary>
        /// <returns>是否加载成功</returns>
        public bool LoadConfig()
        {
            try
            {
                if (!File.Exists(m_ConfigFile))
                {
                    Console.WriteLine("配置文件不存在，将创建默认配置文件: " + m_ConfigFile);
                    CreateDefaultConfig();
                    return false;
                }

                m_DeviceList.Clear();

                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(m_ConfigFile);

                XmlNodeList deviceNodes = xmlDoc.SelectNodes("//Devices/Device");
                foreach (XmlNode deviceNode in deviceNodes)
                {
                    DeviceInfo device = new DeviceInfo();
                    
                    device.IP = deviceNode.SelectSingleNode("IP").InnerText;
                    device.UserName = deviceNode.SelectSingleNode("UserName").InnerText;
                    device.Password = deviceNode.SelectSingleNode("Password").InnerText;
                    
                    XmlNode portNode = deviceNode.SelectSingleNode("Port");
                    if (portNode != null)
                    {
                        ushort port;
                        if (ushort.TryParse(portNode.InnerText, out port))
                        {
                            device.Port = port;
                        }
                    }
                    
                    XmlNode enabledNode = deviceNode.SelectSingleNode("Enabled");
                    if (enabledNode != null)
                    {
                        bool enabled;
                        if (bool.TryParse(enabledNode.InnerText, out enabled))
                        {
                            device.Enabled = enabled;
                        }
                    }
                    
                    XmlNode nameNode = deviceNode.SelectSingleNode("Name");
                    if (nameNode != null)
                    {
                        device.Name = nameNode.InnerText;
                    }
                    
                    XmlNode remarkNode = deviceNode.SelectSingleNode("Remark");
                    if (remarkNode != null)
                    {
                        device.Remark = remarkNode.InnerText;
                    }
                    
                    XmlNode deviceIDNode = deviceNode.SelectSingleNode("DeviceID");
                    if (deviceIDNode != null)
                    {
                        device.DeviceID = deviceIDNode.InnerText;
                    }
                    
                    XmlNode areaIDNode = deviceNode.SelectSingleNode("AreaID");
                    if (areaIDNode != null)
                    {
                        device.AreaID = areaIDNode.InnerText;
                    }
                    
                    XmlNode deviceNameNode = deviceNode.SelectSingleNode("DeviceName");
                    if (deviceNameNode != null)
                    {
                        device.DeviceName = deviceNameNode.InnerText;
                    }
                    
                    m_DeviceList.Add(device);
                }

                Console.WriteLine("成功加载 " + m_DeviceList.Count + " 个设备配置");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("加载配置文件失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 创建默认配置文件
        /// </summary>
        private void CreateDefaultConfig()
        {
            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                XmlDeclaration xmlDeclaration = xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", null);
                xmlDoc.AppendChild(xmlDeclaration);

                XmlElement rootElement = xmlDoc.CreateElement("Devices");
                xmlDoc.AppendChild(rootElement);

                XmlElement deviceElement = xmlDoc.CreateElement("Device");
                rootElement.AppendChild(deviceElement);

                XmlElement ipElement = xmlDoc.CreateElement("IP");
                ipElement.InnerText = "192.168.0.164";
                deviceElement.AppendChild(ipElement);

                XmlElement userNameElement = xmlDoc.CreateElement("UserName");
                userNameElement.InnerText = "admin";
                deviceElement.AppendChild(userNameElement);

                XmlElement passwordElement = xmlDoc.CreateElement("Password");
                passwordElement.InnerText = "scyzkj123456";
                deviceElement.AppendChild(passwordElement);

                XmlElement portElement = xmlDoc.CreateElement("Port");
                portElement.InnerText = "8000";
                deviceElement.AppendChild(portElement);

                XmlElement enabledElement = xmlDoc.CreateElement("Enabled");
                enabledElement.InnerText = "true";
                deviceElement.AppendChild(enabledElement);

                XmlElement nameElement = xmlDoc.CreateElement("Name");
                nameElement.InnerText = "门禁1";
                deviceElement.AppendChild(nameElement);

                XmlElement remarkElement = xmlDoc.CreateElement("Remark");
                remarkElement.InnerText = "默认门禁设备";
                deviceElement.AppendChild(remarkElement);
                
                XmlElement deviceIDElement = xmlDoc.CreateElement("DeviceID");
                deviceIDElement.InnerText = "8f283fe3ca6947fdaba16db6ef3a7914";
                deviceElement.AppendChild(deviceIDElement);
                
                XmlElement areaIDElement = xmlDoc.CreateElement("AreaID");
                areaIDElement.InnerText = "root000000";
                deviceElement.AppendChild(areaIDElement);
                
                XmlElement deviceNameElement = xmlDoc.CreateElement("DeviceName");
                deviceNameElement.InnerText = "测试门禁164";
                deviceElement.AppendChild(deviceNameElement);

                xmlDoc.Save(m_ConfigFile);
                Console.WriteLine("已创建默认配置文件: " + m_ConfigFile);
                Console.WriteLine("请编辑配置文件，添加或修改门禁设备信息，然后重新启动程序");
            }
            catch (Exception ex)
            {
                Console.WriteLine("创建默认配置文件失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 初始化服务
        /// </summary>
        /// <returns>是否初始化成功</returns>
        public bool Initialize()
        {
            if (m_bInitialized)
            {
                return true;
            }

            if (!LoadConfig())
            {
                return false;
            }

            // 初始化SDK，只需要初始化一次
            bool result = CHCNetSDK.NET_DVR_Init();
            if (result)
            {
                CHCNetSDK.NET_DVR_SetLogToFile(3, "./SdkLog/", true);
                Console.WriteLine("SDK初始化成功");
            }
            else
            {
                Console.WriteLine("SDK初始化失败，错误码：" + CHCNetSDK.NET_DVR_GetLastError());
                return false;
            }

            m_bInitialized = true;
            return true;
        }

        /// <summary>
        /// 全局报警回调函数
        /// </summary>
        public void GlobalMsgCallback(int lCommand, ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            try
            {
                // 处理门禁主机报警信息
                if (lCommand == CHCNetSDK.COMM_ALARM_ACS)
                {
                    // 获取设备IP地址
                    string deviceIP = "";
                    if (pAlarmer.sDeviceIP != null && pAlarmer.sDeviceIP.Length > 0)
                    {
                        deviceIP = Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');
                    }

                    // 查找对应的设备信息
                    DeviceInfo deviceInfo = null;
                    foreach (DeviceInfo device in m_DeviceList)
                    {
                        if (device.IP == deviceIP)
                        {
                            deviceInfo = device;
                            break;
                        }
                    }

                    // 查找对应的服务实例
                    foreach (ACSEventService service in m_ServiceList)
                    {
                        // 调用对应服务的处理函数
                        service.ProcessCommAlarmAcs(pAlarmInfo, dwBufLen, ref pAlarmer);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("全局回调函数异常：" + ex.Message);
            }
        }

        /// <summary>
        /// 启动服务
        /// </summary>
        public void Start()
        {
            if (m_bRunning)
            {
                Console.WriteLine("服务已经在运行中");
                return;
            }

            if (!Initialize())
            {
                return;
            }

            m_ServiceList.Clear();

            // 注册全局回调函数
            m_MsgCallback = new CHCNetSDK.MSGCallBack(GlobalMsgCallback);
            bool bRet = CHCNetSDK.NET_DVR_SetDVRMessageCallBack_V30(m_MsgCallback, IntPtr.Zero);
            if (!bRet)
            {
                uint nErr = CHCNetSDK.NET_DVR_GetLastError();
                Console.WriteLine("设置全局回调函数失败，错误码：" + nErr);
                return;
            }

            foreach (DeviceInfo device in m_DeviceList)
            {
                if (device.Enabled)
                {
                    ACSEventService service = new ACSEventService(device.IP, device.UserName, device.Password, device.Port);
                    
                    // 不需要再次初始化SDK，因为已经在Initialize方法中初始化过了
                    if (service.Login() && service.SetupAlarmChan(m_MsgCallback))
                    {
                        m_ServiceList.Add(service);
                        string deviceInfo = string.Format("设备 {0} [{1}] ({2}) 启动成功", 
                            device.IP, 
                            string.IsNullOrEmpty(device.Name) ? "无名称" : device.Name, 
                            string.IsNullOrEmpty(device.Remark) ? "无备注" : device.Remark);
                        Console.WriteLine(deviceInfo);
                        
                        // 显示设备的额外信息
                        Console.WriteLine(string.Format("  设备ID: {0}", 
                            string.IsNullOrEmpty(device.DeviceID) ? "未设置" : device.DeviceID));
                        Console.WriteLine(string.Format("  区域ID: {0}", 
                            string.IsNullOrEmpty(device.AreaID) ? "未设置" : device.AreaID));
                        Console.WriteLine(string.Format("  设备名称: {0}", 
                            string.IsNullOrEmpty(device.DeviceName) ? "未设置" : device.DeviceName));
                    }
                    else
                    {
                        string deviceInfo = string.Format("设备 {0} [{1}] ({2}) 启动失败", 
                            device.IP, 
                            string.IsNullOrEmpty(device.Name) ? "无名称" : device.Name, 
                            string.IsNullOrEmpty(device.Remark) ? "无备注" : device.Remark);
                        Console.WriteLine(deviceInfo);
                    }
                }
                else
                {
                    Console.WriteLine("设备 " + device.IP + " (" + device.Remark + ") 已禁用，跳过");
                }
            }

            if (m_ServiceList.Count > 0)
            {
                m_bRunning = true;
                Console.WriteLine("多门禁事件监控服务已启动，共 " + m_ServiceList.Count + " 个设备");
            }
            else
            {
                Console.WriteLine("没有可用的门禁设备，服务未启动");
                CHCNetSDK.NET_DVR_Cleanup();
            }
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public void Stop()
        {
            if (!m_bRunning)
            {
                return;
            }

            foreach (ACSEventService service in m_ServiceList)
            {
                service.Stop();
            }

            m_ServiceList.Clear();
            m_bRunning = false;

            // 清理SDK资源
            CHCNetSDK.NET_DVR_Cleanup();
            Console.WriteLine("多门禁事件监控服务已停止");
        }
    }
}