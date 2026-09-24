# Shared Blueprints / 共享蓝图库

供《Spaceflight Simulator》PC Steam 版朋友小组使用。成员加入同一个工作区后，在建造界面发布自己已经保存的蓝图、浏览其他成员的蓝图，并导入一份本地副本。Mac 和 Windows 共用同一个服务器；不会自动覆盖本地蓝图。

## 当前版本

- 游戏版本目标：`1.6.00.16`。每张蓝图须包含 `Blueprint.txt` 和 `Version.txt`。
- 入口：建造界面顶部 **New** 左侧的 **Workspace** 按钮，或 `F8`。蓝图库以模态面板打开，可按标题拖动；建造拖动输入在面板打开期间停用。
- 发布只读取已保存蓝图。导入在 `Saving/Blueprints` 下创建新文件夹；遇到重名自动追加编号。
- 用户名和密码对应一个成员账号；Mac、Windows 和网页各有独立登录会话。游戏只保存设备令牌，不保存密码。同一账号可加入多个工作区，并在游戏的 **Workspaces** 页或网页切换；各工作区的成员、加入码和蓝图独立。
- 成员网页：[共享蓝图库](https://hanson07101.top/blueprints/)；管理员网页：[管理后台](https://hanson07101.top/blueprints/admin/)。
- Mac 游戏已确认模组加载、正式账号登录、发布和导入副本且原蓝图保留。0.4.0 的无遮罩面板、完整状态栏与跨工作区切换也已由用户在游戏内验收；Windows 实机加载仍需验收。

## 本地构建

Mac Steam 安装已存在时：

```sh
dotnet build client/BlueprintWorkspace.csproj -c Release
./install-macos.sh
```

Windows 使用 .NET Framework 4.8 开发环境构建，指定游戏的托管程序集目录：

```powershell
dotnet build client/BlueprintWorkspace.csproj -c Release /p:SFSManagedDir="C:\path\to\Spaceflight Simulator Game\Spaceflight Simulator_Data\Managed"
```

当前推荐交付给 Windows 端 Codex 的是 [0.4.0 + BT-014 三 Mod 手动交接包](outputs/SFS-Windows-Manual-Handoff-0.4.0-BT-014-2026-09-23.zip)，包含 Shared Blueprints、Mac 已验收的 BuildTools BT-014、前置 UITools 1.1.6、源码和手动说明，不含自动安装器。解压后先读包内 `START_HERE.md`，由 Windows 端根据实际游戏目录手动安装和验收。此前的 BT-006、BT-005、0.4.0 和 0.3.3 交接包保留作历史基线。

## 服务端

服务端用 Go 1.26 和 `golang.org/x/crypto` 构建。先在私有数据目录初始化工作区：

```sh
cd server
go build -o blueprint-server .
./blueprint-server init -data ./data -name Friends
./blueprint-server serve -data ./data -listen 127.0.0.1:8787
```

`init` 只运行一次，用来建立第一个工作区 `Friends`。之后拥有者可在网页管理后台新建工作区；新加入码只在创建时显示，应私下告知成员。已有账号可凭加入码加入更多工作区。账号密码只保存 Argon2id 摘要；网页使用 `HttpOnly`、`Secure`、`SameSite=Strict` Cookie；游戏只保存设备令牌，服务端仅保存令牌摘要。服务器数据目录应限制为服务账户可读，并纳入备份。

2026-09-23 的早期测试成员与蓝图已从在线数据目录移走，在线工作区按正式账号模型重新初始化。旧测试加入码和令牌在新工作区无效。

如果加入码需要更换，可在后台选择工作区后操作。命令行方式须先停止服务，执行 `blueprint-server rotate-invite -data /var/lib/blueprint-workspace -workspace <工作区ID>`，然后启动服务。只有一个工作区时可省略 `-workspace`。旧码立即失效，已加入成员的令牌保持有效。

**对公网必须启用 HTTPS。** 当前部署使用 `https://hanson07101.top/blueprints`，新服务单独监听 `127.0.0.1:8787`，Caddy 只增加 `/blueprints/*` 路由。其他站点路由保持原样。通用的独立子域名方案：

```caddyfile
blueprints.example.com {
    reverse_proxy 127.0.0.1:8787
}
```

客户端只允许 HTTPS，开发时允许 `http://localhost` 或 `http://127.0.0.1`。健康检查：`GET /api/health`。`GET /api/workspaces` 列出当前账号的工作区，`POST /api/workspaces` 由拥有者创建，`POST /api/workspaces/join` 用加入码加入；请求头 `X-Workspace-ID` 选择蓝图库与管理作用域，旧单工作区客户端仍默认进入第一个工作区。成员接口还提供注册、登录、会话退出、蓝图上传/列表/下载；管理员接口提供成员停用/恢复、角色变更、加入码更新、蓝图归档/恢复。服务器对上传大小和基本蓝图 JSON 结构做验证。

## 验收

1. 两台 PC 分别加入同一个工作区。
2. A 保存蓝图后在 **My saved blueprints** 发布；B 在 **Workspace library** 刷新后看到作者和名称。
3. B 点击 **Import copy**，重新打开游戏的载入蓝图列表，确认可载入且 A、B 原有蓝图没有被改动。
4. 同名蓝图重复导入，确认出现新文件夹而不是覆盖旧文件夹。
5. 断网时确认只显示错误，已有本地蓝图仍可使用。
6. 同一账号加入第二个工作区后切换，确认两个工作区的蓝图、成员和加入码分别生效。

目前没有自动同步、多人同时编辑、蓝图图片预览或移动端游戏模组。网页可在移动浏览器查看蓝图库。加入码只发给可信成员；后台停用成员时会同时更换加入码。
