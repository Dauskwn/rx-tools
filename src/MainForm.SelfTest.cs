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
            sb.AppendLine("form " + ClientSize.Width + "x" + ClientSize.Height);
            for (int t = 0; t < tabs.TabPages.Count; t++)
            {
                TabPage pg = tabs.TabPages[t];
                sb.AppendLine();
                sb.AppendLine("[tab " + t + "] " + pg.Text + "  (" + pg.Controls.Count + " 个直接子控件)");
                DumpControls(sb, pg, "  ", 0);
            }
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
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
        string raw = "引号\"反斜杠\\换行\n制表\t中文";
        string esc = JsonEsc(raw);
        chk("JSON 往返", JsonUnesc(esc) == raw, "得到 " + Esc0(JsonUnesc(esc)));
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

            chk("更新下载 + SHA256 校验通过", DownloadAndVerify(fileUrl, goodSha, dest) && File.Exists(dest), "");
            chk("更新下载：哈希不对时拒绝", !DownloadAndVerify(fileUrl, new string('0', 64), Path.Combine(tmpDir, "bad.bin")), "");
            chk("更新下载：空哈希时拒绝", !DownloadAndVerify(fileUrl, "", Path.Combine(tmpDir, "emptysha.bin")), "");

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
                HKDefVk.Length == 8 && HKDefMod.Length == 8 && HKFunc.Length == 8 && HKTip.Length == 8, "");

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
            chk("公开版更新日志与独立更新源配置检查",
                CHANGELOG.Length == 5
                && CHANGELOG[0].StartsWith("1.8.0|", StringComparison.Ordinal)
                && UPDATE_URL_DEFAULT == "https://rx-tools.pages.dev/version.json"
                && sawWarnLine && strippedWarn.StartsWith("【重要提醒】", StringComparison.Ordinal)
                && !string.Join("\n", CHANGELOG).Contains("!!"), "");



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
            File.WriteAllText(Path.Combine(dir, "selftest.log"), sb.ToString(), new UTF8Encoding(true));
        }
        catch { }
        Console.WriteLine(sb.ToString());
        return fail;
    }
}
