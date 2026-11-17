# RX 工具箱

一款专为 Garry's Mod 玩家打造的轻量级绿色辅助工具。
单文件绿色程序：C# 5 + .NET Framework 4.8 + WinForms，零第三方运行时依赖。

---

## 功能

- **自动挤服助手**：支持「手动档（连续自动回车）」「单一 IP」「收藏轮询」三模式；集成 A2S UDP 服务器实时人数 / 地图探测，并利用 Windows 10/11 系统自带 OCR 后台识别进服状态后自动停止
- **屏幕外置准星**：桌面置顶全透明点击穿透窗口，内置 6 种矢量准星样式与 8 种高对比色（线宽/臂长/间隙/中心点全可调），支持导入 PNG 或剪贴板粘贴自定义准星图片
- **倒计时（时间链）**：点击穿透悬浮数字时钟，内置标准对局示例方案并支持新建/复制方案；可配置基于剩余时间触发的颜色变化、低/中/高频闪烁及描边提示标注
- **自定义快捷文本**：随手保存常用聊天或快捷文本卡片（`custom.txt`），支持关键词实时筛选（按 Esc 清空）、排序、多行编辑（Ctrl+S）及单击一键复制到剪贴板
- **主题与全局热键**：支持「护眼浅色（默认）/ 跟随系统 / 夜间深色」三档主题及全弹窗适配；提供 8 项可自定义全局热键与 `Ctrl+F10` 一键临时关闭/恢复热键
- **纯 C# 原地更新**：同目录 `.upd` 下载与 64 位 SHA256 校验、`.old` 原地改名替换与异常回滚保护，失败或只读目录时可跳转至 [GitHub Releases](https://github.com/Dauskwn/rx-tools/releases) 手动下载

## 构建

只需 Windows + 自带的 .NET Framework 4.8（`csc.exe`），**不需要**安装 .NET SDK：

```powershell
pwsh -NoProfile -File build\build.ps1
```

产物位于 `dist\Rx工具箱.exe`。

## 自检与调试

```powershell
$env:RX_SELFTEST = "1"
dist\Rx工具箱.exe        # 不建窗口运行核心单元测试（27 项），结果写 selftest.log，失败退出码 1

$env:RX_DUMP_UI = "1"
dist\Rx工具箱.exe        # 将各标签页控件坐标写入 dump-ui.log

dist\Rx工具箱.exe --dev  # 独立多实例调试启动，方便与常规实例同时运行
```

## 隐私

本程序不收集、上传任何用户个人数据。唯一主动联网的环节为版本检查与更新下载：

- 检查更新：向静态更新源 `https://rx-tools.pages.dev/version.json` 拉取版本号、安装包地址与 SHA256 摘要（支持在「关于」页取消勾选自动检查）。服务端仅在边缘节点对请求 IP 做单向不可逆短哈希按日去重，用于粗略评估活跃人数与更新下载量，不保存明文 IP 或任何个人信息。

## Code signing policy

**Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).**

- Committers and reviewers: [@Dauskwn](https://github.com/Dauskwn) (repository owner)
- Approvers: [@Dauskwn](https://github.com/Dauskwn)
- Privacy policy: This program will not transfer any information to other networked systems unless
  specifically requested by the user or by the optional startup version-check setting. The only network
  activity (besides user-initiated UDP A2S server queries) is fetching the static update manifest and
  verified release binary from the configured release endpoint.
- Build: `.github/workflows/build.yml` compiles `src/*.cs` + `build/program.cs` with the `csc.exe`
  bundled with .NET Framework 4.8, runs `RX_SELFTEST=1` verification, then submits the artifact for signing.
- Status: signing application in progress.

## 许可

[MIT](LICENSE)。允许自由使用、修改与再分发（需保留原作者版权及许可声明）。本工具完全免费开源，官方未授权任何收费版本，请勿从付费或倒卖渠道购买。
