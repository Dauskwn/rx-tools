using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WSize = Windows.Foundation.Size;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

public partial class MainForm
{
    void DumpUi()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("RX 工具箱 UI dump  " + APP_VERSION + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("title " + Text);
            sb.AppendLine("form " + ClientSize.Width + "x" + ClientSize.Height);
            foreach (var kv in navPages)
            {
                Control pg = kv.Value;
                if (pg == null) continue;
                sb.AppendLine();
                sb.AppendLine("[nav " + kv.Key + "] " + pg.Text + "  (" + pg.Controls.Count + " 个直接子控件)");
                DumpControls(sb, pg, "  ", 0);
            }
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (!CanWriteDir(dir))
            {
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RXToolbox");
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(Path.Combine(dir, "dump-ui.log"), sb.ToString(), new UTF8Encoding(true));
        }
        catch { }
    }

    static void DumpControls(StringBuilder sb, Control parent, string indent, int depth)
    {
        if (depth > 3) return;
        foreach (Control c in parent.Controls)
        {
            string text = c.Text ?? "";
            if (text.Length > 40) text = text.Substring(0, 40) + "…";
            text = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            sb.Append(indent).Append(c.GetType().Name).Append(" \"").Append(text).Append("\"");
            sb.Append("  x=").Append(c.Left).Append(" y=").Append(c.Top);
            sb.Append(" w=").Append(c.Width).Append(" h=").Append(c.Height);
            sb.Append(" vis=").Append(c.Visible ? 1 : 0);
            if (!c.Enabled) sb.Append(" disabled");
            sb.AppendLine();
            if (c.Controls.Count > 0) DumpControls(sb, c, indent + "  ", depth + 1);
        }
    }

    public static int SelfTest()
    {
        int pass = 0, fail = 0;
        var sb = new StringBuilder();
        sb.AppendLine("RX 工具箱自检 " + APP_VERSION + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();

        Action<string, bool, string> chk = delegate(string name, bool ok, string detail)
        {
            if (ok) { pass++; sb.AppendLine("  PASS  " + name); }
            else { fail++; sb.AppendLine("  FAIL  " + name + "    " + detail); }
        };


        // ---- 4. JSON 转义 ----
        string raw = "引号\"反斜杠\\换行\n制表\t控制\u000b\u000c中文";
        string esc = JsonEsc(raw);
        chk("JSON 往返", JsonUnesc(esc) == raw && esc.IndexOf("\\u000b", StringComparison.Ordinal) >= 0, "得到 " + Esc0(JsonUnesc(esc)));
        chk("JSON 字段读取", JsonField("{\"a\":\"x\\ny\",\"b\":2}", "a") == "x\ny", "");
        string pathSnap = "C:\\Games\\SteamLibrary\\steamapps\\common\\GarrysMod\\garrysmod\\addons\\sample\\file.txt";
        string pathJson = "{\"ok\":true,\"data\":\"" + JsonEsc(pathSnap) + "\"}";
        string pathRead = JsonField(pathJson, "data");
        chk("JSON 反斜杠路径不二次反转义", pathRead == pathSnap, "得到 " + Esc0(pathRead));

        // ---- 5. 版本比较 ----
        chk("版本 1.6.1 > 1.6.0", CompareVersion("1.6.1", "1.6.0") > 0, "");
        chk("版本 1.6.10 > 1.6.9", CompareVersion("1.6.10", "1.6.9") > 0, "");
        chk("版本 1.6.0 == 1.6.0", CompareVersion("1.6.0", "1.6.0") == 0, "");
        chk("版本 1.5.9 < 1.6.0", CompareVersion("1.5.9", "1.6.0") < 0, "");


        // ---- 自动更新：下载 + 校验（用本地 file:// 真实走一遍） ----
        try
        {
            string tmpDir = Path.Combine(Path.GetTempPath(), "rx_selftest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            string src = Path.Combine(tmpDir, "payload.bin");
            string payload = "RX toolbox data payload " + DateTime.Now.Ticks;
            File.WriteAllText(src, payload);
            string goodSha = Sha256File(src);
            string dest = Path.Combine(tmpDir, "got.bin");
            string fileUrl = new Uri(src).AbsoluteUri;

            chk("更新下载 + SHA256 校验通过", DownloadAndVerify(fileUrl, goodSha, dest, null, true) && File.Exists(dest), "");
            chk("更新下载：哈希不对时拒绝", !DownloadAndVerify(fileUrl, new string('0', 64), Path.Combine(tmpDir, "bad.bin"), null, true), "");
            chk("更新下载：空哈希时拒绝", !DownloadAndVerify(fileUrl, "", Path.Combine(tmpDir, "emptysha.bin"), null, true), "");
            chk("更新下载：拒绝非本机 http://，允许 https:// 与本机回环端口",
                !IsHttpsUrl("http://rx-tools.pages.dev/a.exe")
                && IsHttpsUrl("https://rx-tools.pages.dev/a.exe")
                && !IsAllowedUpdateUrl("http://example.com/a.exe")
                && !IsAllowedUpdateUrl("http://192.168.1.10/a.exe")
                && IsAllowedUpdateUrl("http://127.0.0.1:18080/version.json")
                && IsAllowedUpdateUrl("http://localhost:18080/a.exe")
                && !DownloadAndVerify("http://example.com/a.exe", goodSha, Path.Combine(tmpDir, "http.bin")), "");
            chk("更新下载：生产默认拒绝 file:// 与 UNC 路径",
                !DownloadAndVerify(fileUrl, goodSha, Path.Combine(tmpDir, "blocked_file.bin"))
                && !DownloadAndVerify("file://attacker/share/evil.exe", goodSha, Path.Combine(tmpDir, "blocked_unc.bin"), null, true), "");
            chk("更新跳转校验：禁止 HTTPS 降级为 HTTP 或回环地址",
                IsAllowedRedirectTarget("https://rx-tools.pages.dev/a", "https://cdn.example.com/b")
                && !IsAllowedRedirectTarget("https://rx-tools.pages.dev/a", "http://cdn.example.com/b")
                && !IsAllowedRedirectTarget("https://rx-tools.pages.dev/a", "http://127.0.0.1:8080/b")
                && IsAllowedRedirectTarget("http://127.0.0.1:18080/a", "http://127.0.0.1:18080/b"), "");
            using (var testBmp = new Bitmap(32, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(testBmp)) g.Clear(Color.Black);
                using (var sb1 = CreateSoftwareBitmapFromBitmap(testBmp, 1))
                using (var sb2 = CreateSoftwareBitmapFromBitmap(testBmp, 2))
                {
                    chk("OCR 零 PNG 位图直传：scale=1 与 scale=2 正常生成 SoftwareBitmap",
                        sb1 != null && sb1.PixelWidth == 32 && sb1.PixelHeight == 16
                        && sb2 != null && sb2.PixelWidth == 64 && sb2.PixelHeight == 32, "");
                }
            }
            Native.LogWarn(null, null);
            Native.LogWarn("selftest", new InvalidOperationException("rate-limit-check"));
            Native.LogWarn("selftest", new InvalidOperationException("rate-limit-check"));
            chk("Native.LogWarn 空参数与限流调用不抛异常", true, "");

            string savedLogPath = Native.LogPath;
            try
            {
                string testLog = Path.Combine(tmpDir, "rx_test.log");
                Native.LogPath = testLog;
                long emptySz = Native.GetLogSizeBytes();
                File.WriteAllBytes(testLog, new byte[12]);
                File.WriteAllBytes(testLog + ".old", new byte[8]);
                long twoSz = Native.GetLogSizeBytes();
                bool cleared = Native.ClearLog();
                long afterSz = Native.GetLogSizeBytes();
                chk("Native.GetLogSizeBytes / ClearLog 统计与清空主备日志准确",
                    emptySz == 0 && twoSz == 20 && cleared && afterSz == 0
                    && !File.Exists(testLog) && !File.Exists(testLog + ".old"), "");
            }
            finally { Native.LogPath = savedLogPath; }

            chk("准星中心偏移钳制 Overlay.ClampOffset 边界正确",
                Overlay.ClampOffset(-999) == Overlay.OffsetMin
                && Overlay.ClampOffset(999) == Overlay.OffsetMax
                && Overlay.ClampOffset(3) == 3, "");

            var cpList = new List<CrosshairPreset>();
            var cpA = new CrosshairPreset
            {
                Name = "狙击红点,微偏", StyleIndex = 2, ColorIndex = 4, Thickness = 3, Length = 12, Gap = 4,
                DotSize = 3, OutlineWidth = 2, OpacityPct = 85, OffsetX = -2, OffsetY = 3, CustomImagePath = ""
            };
            var cpB = new CrosshairPreset
            {
                Name = "自定义;PNG", StyleIndex = 0, ColorIndex = 0, Thickness = 2, Length = 10, Gap = 5,
                DotSize = 1, OutlineWidth = 1, OpacityPct = 90, OffsetX = 1, OffsetY = -1,
                CustomImagePath = @"C:\dir\cross,semi;test.png"
            };
            cpList.Add(cpA);
            cpList.Add(cpB);
            string cpSer = CrosshairPreset.SerializePresets(cpList);
            var cpBack = CrosshairPreset.DeserializePresets(cpSer);
            chk("准星方案 CrosshairPreset 序列化往返无损（含特殊标点与偏移）",
                cpBack.Count == 2
                && cpBack[0].Name == "狙击红点,微偏" && cpBack[0].OffsetX == -2 && cpBack[0].OffsetY == 3 && cpBack[0].OpacityPct == 85
                && cpBack[1].Name == "自定义;PNG" && cpBack[1].CustomImagePath == @"C:\dir\cross,semi;test.png", "");

            var cpDefaults = CrosshairPreset.DeserializePresets("broken-data");
            using (var ovTest = new Overlay())
            {
                cpDefaults[1].ApplyScalarToOverlay(ovTest);
                bool matchBefore = cpDefaults[1].MatchesOverlay(ovTest);
                ovTest.OffsetX = 2;
                bool matchAfter = cpDefaults[1].MatchesOverlay(ovTest);
                chk("准星默认方案 4 套加载与 MatchesOverlay 匹配/改动检测",
                    cpDefaults.Count == 4 && matchBefore && !matchAfter, "");
            }

            using (var strip = new CrosshairPresetStrip())
            {
                strip.Size = new Size(498, 74);
                strip.Presets = cpDefaults;
                var rc0 = strip.GetCardRect(0);
                var rc3 = strip.GetCardRect(3);
                chk("CrosshairPresetStrip 卡片命中测试与单实例唤醒消息常量检查",
                    strip.HitTest(new Point(rc0.X + 4, rc0.Y + 4)) == 0
                    && strip.HitTest(new Point(rc3.X + 4, rc3.Y + 4)) == 3
                    && Native.WM_RX_WAKE == 0x8001, "");
            }

            chk("倒计时格式化 MM:SS",
                CountdownOverlay.FormatMmSs(900) == "15:00"
                && CountdownOverlay.FormatMmSs(1020) == "17:00"
                && CountdownOverlay.FormatMmSs(6000) == "100:00"
                && CountdownOverlay.FormatMmSs(90) == "01:30"
                && CountdownOverlay.FormatMmSs(0) == "00:00"
                && CountdownOverlay.FormatMmSs(-5) == "00:00", "");
            chk("时间链空字符串反序列化为空列表",
                TimerChainPreset.DeserializeNodes("").Count == 0, "");
            var sampleNode = TimerChainPreset.MakeNode(1, 870, 810, 0, 2, 1, "刷支援");
            var sampleList = new List<TimerChainNode>();
            sampleList.Add(sampleNode);
            var deSampleList = TimerChainPreset.DeserializeNodes(TimerChainPreset.SerializeNodes(sampleList));
            chk("时间链单条节点序列化往返",
                deSampleList.Count == 1 && deSampleList[0].TriggerMode == 1
                && deSampleList[0].StartSec == 870 && deSampleList[0].EndSec == 810
                && deSampleList[0].FlashMode == 1 && deSampleList[0].LabelText == "刷支援", "");
            chk("热键配置表长度一致（8 项）",
                HK_COUNT == 8 && HKDefVk.Length == 8 && HKDefMod.Length == 8 && HKFunc.Length == 8 && HKTip.Length == 8, "");

            var defPresets = TimerChainPreset.CreateDefaultPresets();
            string serChains = TimerChainPreset.SerializePresets(defPresets);
            var dePresets = TimerChainPreset.DeserializePresets(serChains);
            chk("时间链预设序列化往返",
                dePresets.Count == 2
                && dePresets[0].Name == "标准对局" && dePresets[0].TotalSec == 1080 && dePresets[0].Nodes.Count == 0
                && dePresets[1].Name == "标准对局(带标注)" && dePresets[1].TotalSec == 1080
                && dePresets[1].Nodes.Count == 14 && dePresets[1].Nodes[1].LabelText == "15:00 轻收开门", "");

            var nStd = dePresets[1].Nodes;
            int tc; int tf; string tl;
            TimerChainPreset.EvaluateTimeline(nStd, 1050, 0, out tc, out tf, out tl);
            chk("时间链 17:30 事件①标注", tc == 0 && tf == 0 && tl == "17:00 事件①", "");
            TimerChainPreset.EvaluateTimeline(nStd, 1040, 0, out tc, out tf, out tl);
            chk("时间链 17:20 恢复常态", tc == 0 && tf == 0 && tl == "", "");
            TimerChainPreset.EvaluateTimeline(nStd, 620, 0, out tc, out tf, out tl);
            chk("时间链 10:20 低频闪烁", tc == 0 && tf == 1 && tl == "", "");
            TimerChainPreset.EvaluateTimeline(nStd, 150, 0, out tc, out tf, out tl);
            chk("时间链 02:30 亮红+低频闪烁+核弹标注叠加", tc == 5 && tf == 1 && tl == "2:00 核弹激活倒计时", "");
            TimerChainPreset.EvaluateTimeline(nStd, 60, 0, out tc, out tf, out tl);
            chk("时间链 01:00 亮红+中频闪烁", tc == 5 && tf == 2 && tl == "", "");
            TimerChainPreset.EvaluateTimeline(nStd, 10, 0, out tc, out tf, out tl);
            chk("时间链 00:10 亮红+高频闪烁", tc == 5 && tf == 3 && tl == "", "");

            // ---- 纯 C# 自更新（参数解析 / 同目录替换 / 回滚 / 清理 .old） ----
            chk("命令行 --after-update 解析",
                Program.ParseAfterUpdatePid(new string[] { "rx.exe", "--after-update", "4321", "--dev" }) == 4321
                && Program.ParseAfterUpdatePid(new string[] { "rx.exe", "--dev" }) == 0
                && Program.ParseAfterUpdatePid(new string[] { "rx.exe", "--after-update", "bad" }) == 0, "");

            string updDir = Path.Combine(tmpDir, "upd_case");
            Directory.CreateDirectory(updDir);
            string exeF = Path.Combine(updDir, "RXToolbox.exe");
            string updExeF = exeF + ".upd";
            File.WriteAllText(exeF, "OLD_EXE");
            File.WriteAllText(updExeF, "NEW_EXE_12345");
            string oldExeU, oldDataU, swapErr;
            bool swapOk = ApplySelfUpdateFiles(
                exeF, updExeF, new FileInfo(updExeF).Length,
                "", "", 0,
                out oldExeU, out oldDataU, out swapErr);
            chk("自更新原地替换成功",
                swapOk
                && File.ReadAllText(exeF) == "NEW_EXE_12345"
                && File.ReadAllText(oldExeU) == "OLD_EXE"
                && !File.Exists(updExeF), swapErr);

            RollbackSelfUpdateFiles(exeF, oldExeU, updExeF, "", "", "");
            chk("自更新启动失败回滚成功",
                File.ReadAllText(exeF) == "OLD_EXE"
                && File.Exists(updExeF), "");

            File.WriteAllText(exeF + ".old.999", "STALE");
            CleanupUpdateArtifacts(exeF, "", false);
            chk("自更新残留 .old/.upd 清理",
                !File.Exists(exeF + ".old")
                && !File.Exists(exeF + ".old.999")
                && !File.Exists(updExeF), "");

            File.WriteAllText(updExeF, "NEW_EXE_SIZE_TEST");
            string dummyOldE, dummyOldD, badErr;
            bool badSwap = ApplySelfUpdateFiles(exeF, updExeF, 999999L, "", "", 0, out dummyOldE, out dummyOldD, out badErr);
            chk("自更新大小校验不符自动拒绝并保留旧版",
                !badSwap && File.ReadAllText(exeF) == "OLD_EXE", badErr);

            bool sawWarnLine;
            string strippedWarn = StripWarnChangelogLine("!!【重要提醒】测试", out sawWarnLine);
            chk("公开版更新日志、无管理后缀与独立更新源配置检查",
                CHANGELOG.Length == 6
                && CHANGELOG[0].StartsWith(APP_VERSION_NUM + "|", StringComparison.Ordinal)
                && APP_EDITION_SUFFIX == ""
                && UPDATE_URL_DEFAULT == "https://rx-tools.pages.dev/version.json"
                && sawWarnLine && strippedWarn.StartsWith("【重要提醒】", StringComparison.Ordinal)
                && !string.Join("\n", CHANGELOG).Contains("!!"), "");



            // ---- V1.9.0 HUD 渲染与下一事件预告 ----
            var next1 = TimerChainPreset.FindNextUpcomingNode(nStd, 1060);
            var next2 = TimerChainPreset.FindNextUpcomingNode(nStd, 960);
            var nextNone = TimerChainPreset.FindNextUpcomingNode(nStd, 10);
            chk("倒计时下一事件预告 FindNextUpcomingNode",
                next1 != null && next1.LabelText == "17:00 事件①"
                && next2 != null && next2.LabelText == "15:00 轻收开门"
                && nextNone == null, "");

            string pngPath = Path.Combine(tmpDir, "alpha_cross.png");
            using (var srcPng = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                for (int py = 0; py < 16; py++)
                    for (int px = 0; px < 16; px++)
                        srcPng.SetPixel(px, py, Color.FromArgb(0, 0, 0, 0));
                srcPng.SetPixel(8, 8, Color.FromArgb(120, 0, 255, 0));
                srcPng.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            using (Bitmap outPng = Overlay.PrepareCrosshairImage(pngPath))
            {
                chk("32-bit 准星 PrepareCrosshairImage 保留 PNG 半透明 Alpha",
                    outPng != null && outPng.GetPixel(0, 0).A == 0 && outPng.GetPixel(8, 8).A == 120,
                    outPng == null ? "null" : ("a=" + outPng.GetPixel(8, 8).A));
            }

            string bmpPath = Path.Combine(tmpDir, "opaque_cross.bmp");
            using (var srcBmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                for (int py = 0; py < 16; py++)
                    for (int px = 0; px < 16; px++)
                        srcBmp.SetPixel(px, py, Color.Black);
                srcBmp.SetPixel(8, 8, Color.White);
                srcBmp.Save(bmpPath, System.Drawing.Imaging.ImageFormat.Bmp);
            }
            using (Bitmap outBmp = Overlay.PrepareCrosshairImage(bmpPath))
            {
                chk("32-bit 准星 PrepareCrosshairImage 不透明图自动抠底",
                    outBmp != null && outBmp.GetPixel(0, 0).A == 0 && outBmp.GetPixel(8, 8).A == 255,
                    outBmp == null ? "null" : ("corner=" + outBmp.GetPixel(0, 0).A + " center=" + outBmp.GetPixel(8, 8).A));
            }

            using (var ovDef = new Overlay())
            using (var cdDef = new CountdownOverlay())
            {
                chk("HUD 默认外观与语义色板检查",
                    ovDef.OutlineWidth == 1 && ovDef.OpacityPct == 100
                    && !cdDef.UseCardStyle
                    && !ThemePalette.Accent.IsEmpty
                    && !ThemePalette.Success.IsEmpty
                    && !ThemePalette.Danger.IsEmpty
                    && ThemePalette.Success.ToArgb() != ThemePalette.Danger.ToArgb(), "");
            }


            bool themeMapOk = (ModeToSettingsThemeIndex(0) == 0 && ModeToSettingsThemeIndex(2) == 1 && ModeToSettingsThemeIndex(1) == 2
                && SettingsThemeIndexToMode(0) == 0 && SettingsThemeIndexToMode(1) == 2 && SettingsThemeIndexToMode(2) == 1);
            for (int tm = 0; tm <= 2; tm++)
            {
                if (SettingsThemeIndexToMode(ModeToSettingsThemeIndex(tm)) != tm) themeMapOk = false;
            }
            int segEvtCount = 0;
            using (var segT = new RxSegmentedControl())
            {
                segT.Items = new string[] { "跟随系统", "护眼浅色", "夜间深色" };
                segT.SelectedIndexChanged += delegate { segEvtCount++; };
                segT.SetSelectedSilent(2);
                segT.SetSelectedSilent(1);
                if (segT.SelectedIndex != 1 || segEvtCount != 0) themeMapOk = false;
            }
            chk("设置页主题分段索引与 themeMode 双向映射且静默同步不触发事件", themeMapOk, "");

            var parsedSrv = ParseRemoteServersText(
                "[{\"addr\":\"10.0.0.1:27015\",\"alias\":\"1服\"},"
                + "{\"addr\":\"10.0.0.1:27015\",\"alias\":\"重复\"},"
                + "{\"addr\":\"bad_addr\",\"alias\":\"x\"},"
                + "{\"addr\":\"10.0.0.2:70000\",\"alias\":\"badport\"},"
                + "{\"addr\":\"play.example.com:27016\",\"alias\":\"域名|含竖线\"}]");
            int addedSrv;
            string[] mergedSrv = MergeOnlineServers(
                new string[] { "10.0.0.1:27015|我的自建备注|旧真实服名", "10.0.0.9:27015" },
                parsedSrv, out addedSrv);
            chk("线上服务器解析与无损合并（保留玩家原有备注与已缓存名称）",
                parsedSrv.Count == 2
                && parsedSrv[0][0] == "10.0.0.1:27015" && parsedSrv[0][1] == "1服"
                && parsedSrv[1][0] == "play.example.com:27016" && parsedSrv[1][1] == "域名 含竖线"
                && addedSrv == 1 && mergedSrv.Length == 3
                && mergedSrv[0] == "10.0.0.1:27015|我的自建备注|旧真实服名"
                && mergedSrv[1] == "10.0.0.9:27015"
                && mergedSrv[2] == "play.example.com:27016|域名 含竖线", "");

            try { Directory.Delete(tmpDir, true); } catch { }
        }
        catch (Exception ex)
        {
            chk("更新下载自检可运行", false, ex.GetType().Name);
        }

        sb.AppendLine();
        sb.AppendLine(string.Format("结果：{0} 通过，{1} 失败", pass, fail));

        try
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (!CanWriteDir(dir))
            {
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RXToolbox");
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(Path.Combine(dir, "selftest.log"), sb.ToString(), new UTF8Encoding(true));
        }
        catch { }
        Console.WriteLine(sb.ToString());
        return fail;
    }
}
