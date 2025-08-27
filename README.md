GetACSEvent（明眸门禁事件监控-控制台 + 内置Web配置）

本项目基于海康威视 HCNetSDK 实时获取门禁事件，并提供内置 Web 页面用于在线查看/编辑配置与查看事件。

功能概览
- 启动即初始化 SDK、按配置自动登录多个门禁并布防。
- 仅打印“未知事件类型(75)”事件，并将事件图片持久化到 D:/Picture/[设备IP]/。
- 事件信息（时间、IP、设备名称、设备ID、区域ID、备注、卡号、员工号等）输出到控制台，并写入内存事件仓库（最近1000条）。
- 内置 Web 服务（HttpListener）：
  - GET `/` 首页导航
  - GET `/events` 返回最近事件（JSON）
  - GET `/config` 返回当前 DeviceConfig.xml
  - GET `/config/edit` 在线编辑配置（表单页）
  - POST `/config` 保存配置（支持表单或直接原始 XML）
- 启动前自动从接口拉取设备信息，筛选后按 IP 合并进本地配置：
  - API：`http://192.168.0.14:5000/api/device`
  - 仅保留：`device_type` = “门禁” 且 `is_important` = “否”
  - 字段映射：`deviceid→DeviceID`、`areaid→AreaID`、`ipaddress→IP`、`device_name→DeviceName/Name`
  - 将接口原始/筛选数据保存到运行目录：`ApiDevice_raw.json`、`ApiDevice_filtered.json`
  - 若某项缺少 `ipaddress`，按模板创建默认设备（见下）。

运行环境
- Windows x64
- .NET Framework 3.5（项目使用旧版框架；VS2022 可编译）
- 海康 SDK 及随项目附带的 DLL 已在 `GetACSEvent/bin` 下准备

构建
1. 使用 Visual Studio 打开解决方案 `ACSEventConsole.sln`，选择 x64 + Debug/Release，直接生成。
2. 或命令行（示例）
   ```bat
   D:\VisualStudio\2022\Community\MSBuild\Current\Bin\MSBuild.exe ACSEventConsole.sln /p:Platform=x64 /p:Configuration=Debug
   ```

运行
生成完成后，运行：
- `GetACSEvent\GetACSEvent\bin\x64\Debug\ACSEventConsole.exe`

首次启动会：
1. 调用接口拉取设备并按 IP 合并到运行目录的 `DeviceConfig.xml`；
2. 启动多门禁监控；
3. 启动内置 Web 服务（优先绑定 `http://localhost:8080/`）。

控制台快捷键：按 ESC 退出。

内置 Web 服务
- 首页：`http://localhost:8080/`
- 查看事件（JSON）：`http://localhost:8080/events`
- 查看配置（XML）：`http://localhost:8080/config`
- 在线编辑：`http://localhost:8080/config/edit`
  - 修改后点击“保存”，保存成功会显示绿色提示；
  - 表单提交类型为 `application/x-www-form-urlencoded`，服务端已正确解码。

若 Web 启动失败并提示“拒绝访问”，可用管理员权限执行（任选一个）：
- 以管理员运行程序；或
- 开放 URLACL（示例）：
  ```bat
  netsh http add urlacl url=http://+:8080/ user=Everyone
  ```

配置文件（DeviceConfig.xml）
运行目录下的 `DeviceConfig.xml` 示例：
```xml
<Devices>
  <Device>
    <IP>192.168.0.164</IP>
    <UserName>admin</UserName>
    <Password>scyzkj123456</Password>
    <Port>8000</Port>
    <Enabled>true</Enabled>
    <Name>前门门禁</Name>
    <Remark>位于大厅前门的门禁设备</Remark>
    <DeviceID>8f283fe3ca6947fdaba16db6ef3a7914</DeviceID>
    <AreaID>root000000</AreaID>
    <DeviceName>测试门禁164</DeviceName>
  </Device>
  <!-- 更多Device节点... -->
</Devices>
```

字段说明
- IP：门禁主机 IP（作为关键字匹配合并）
- UserName/Password：登录账号/密码（默认 Password 为 `scyzkj123456`，可在网页编辑修改）
- Port：登录端口，默认 8000
- Enabled：是否启用
- Name / DeviceName：设备名称（两个字段都会用于显示）
- Remark：备注
- DeviceID / AreaID：外部系统的设备/区域标识

启动前 API 合并策略
- 以 IP 为关键字，若本地存在同 IP，只更新映射字段（DeviceID/AreaID/DeviceName/Name），其余字段（如 Password、Remark）保持不变；
- 若本地不存在该 IP 则新增；
- 若接口项缺少 `ipaddress`，将按模板创建默认设备：
  - `IP=192.168.0.164`、`UserName=admin`、`Password=scyzkj123456`、`Port=8000`、`Enabled=true`
  - `Name/DeviceName`：优先使用 `device_name`，否则使用“前门门禁/测试门禁164”
  - `Remark=位于大厅前门的门禁设备`
  - `DeviceID`：优先 `deviceid`，否则默认 `8f283fe3ca6947fdaba16db6ef3a7914`
  - `AreaID`：优先 `areaid`，否则默认 `root000000`

事件与图片
- 仅打印 `未知事件类型(75)`；
- 图片保存路径：`D:/Picture/[设备IP]/[时间信息]_Major[5]_Minor[75]_AcsEvent.jpg`
- 事件结构在内存中保存最近 1000 条，可通过 `/events` 查看。

常见问题
- Web 拒绝访问：参考“内置 Web 服务”章节，使用管理员权限或执行 URLACL。
- XML 无法保存：确认 XML 根节点正确，且不是非法字符编码（UTF-8）。
- 配置未生效：确认修改的是运行目录下的 `DeviceConfig.xml`（可在 `/config/edit` 页面直接编辑）。

目录结构（关键文件）
- `GetACSEvent/ACSEventConsole.cs`：程序入口，启动 API 更新、门禁监控、Web 服务
- `GetACSEvent/ACSEventService.cs`：门禁 SDK 登录与事件回调
- `GetACSEvent/ACSEventMultiDeviceService.cs`：多设备启动、共享事件仓库
- `GetACSEvent/SimpleWebServer.cs`：内置 Web 服务
- `GetACSEvent/DeviceConfigUpdater.cs`：接口拉取与合并到配置
- `GetACSEvent/EventStore.cs`：内存事件存储（最近1000条）
- `GetACSEvent/CHCNetSDK.cs`：海康 SDK P/Invoke 定义

免责声明
- 请确保设备访问与接口调用在授权范围内；
- 若用于生产环境，请对配置数据的读写做备份与权限控制。

