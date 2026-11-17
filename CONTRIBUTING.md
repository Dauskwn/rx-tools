# 贡献指南

## 代码约束

本项目刻意保持「单文件分发 + 零第三方依赖」（源码按模块拆分为 `src/*.cs` 与 `build/program.cs`），请在这些前提下改动：

```
语言      C# 5（不能使用字符串插值、?.、nameof、表达式体成员等 C# 6+ 语法）
UI        WinForms
运行时    .NET Framework 4.8（Windows 自带）
编译      csc.exe /target:winexe /platform:x64
第三方库  无
```

## 不要做的事

- 不启用 `SetProcessDPIAware`：本程序使用绝对坐标布局，启用后高 DPI 缩放下控件会错位
- 不改动现有控件的坐标与尺寸，除非在 100% / 125% / 150% 缩放下通过 `RX_DUMP_UI=1` 验证过
- 不在代码里写死私人服务器 IP 或敏感凭据

## 提交

```
feat(模块): 简述
fix(模块): 简述
chore: 简述
```

改版本号用 `pwsh -File build\bump-version.ps1 1.8.0`，会同步更新源码常量与 `VERSION` 文件。
