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
    // ===== A2S 网络查询引擎（带超时 DNS 解析与分包重组）=====
    static byte[] A2SReq(byte[] challenge)
    {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0, 4);
        byte[] q = Encoding.ASCII.GetBytes("TSource Engine Query");
        ms.Write(q, 0, q.Length);
        ms.WriteByte(0);
        if (challenge != null) ms.Write(challenge, 0, 4);
        return ms.ToArray();
    }

    static IPEndPoint ResolveEndPoint(string host, int port, int timeoutMs)
    {
        IPAddress ip;
        if (IPAddress.TryParse(host, out ip))
            return new IPEndPoint(ip, port);
        try
        {
            IAsyncResult ar = Dns.BeginGetHostAddresses(host, null, null);
            using (WaitHandle wh = ar.AsyncWaitHandle)
            {
                if (!wh.WaitOne(Math.Max(300, timeoutMs)))
                    return null;
            }
            IPAddress[] addrs = Dns.EndGetHostAddresses(ar);
            for (int i = 0; i < addrs.Length; i++)
                if (addrs[i].AddressFamily == AddressFamily.InterNetwork)
                    return new IPEndPoint(addrs[i], port);
            if (addrs.Length > 0) return new IPEndPoint(addrs[0], port);
        }
        catch { }
        return null;
    }

    static bool IsSimpleHeader(byte[] buf, byte type)
    {
        return buf != null && buf.Length >= 5
            && buf[0] == 0xFF && buf[1] == 0xFF && buf[2] == 0xFF && buf[3] == 0xFF
            && buf[4] == type;
    }

    static bool IsSplitHeader(byte[] buf)
    {
        return buf != null && buf.Length > 12
            && buf[0] == 0xFE && buf[1] == 0xFF && buf[2] == 0xFF && buf[3] == 0xFF;
    }

    static bool SameEndPoint(IPEndPoint a, IPEndPoint b)
    {
        return a != null && b != null && a.Port == b.Port && a.Address.Equals(b.Address);
    }

    static byte[] ReceiveA2SPayload(UdpClient udp, IPEndPoint ep, byte[] first)
    {
        if (first == null || first.Length < 5) return null;
        if (first[0] == 0xFF && first[1] == 0xFF && first[2] == 0xFF && first[3] == 0xFF)
            return first;
        if (!IsSplitHeader(first)) return null;
        if ((first[7] & 0x80) != 0) return null; // BZip2 压缩分片不直接拼接

        int total = first[8];
        int idx0 = first[9];
        if (total <= 0 || total > 16 || idx0 < 0 || idx0 >= total) return null;

        byte id0 = first[4], id1 = first[5], id2 = first[6], id3 = first[7];
        byte[][] chunks = new byte[total][];
        chunks[idx0] = new byte[first.Length - 12];
        Array.Copy(first, 12, chunks[idx0], 0, chunks[idx0].Length);
        int received = 1;

        int guard = 0;
        while (received < total && guard < 24)
        {
            guard++;
            byte[] rr;
            IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
            try { rr = udp.Receive(ref from); }
            catch { break; }
            if (!SameEndPoint(from, ep) || !IsSplitHeader(rr)) continue;
            if (rr[4] != id0 || rr[5] != id1 || rr[6] != id2 || rr[7] != id3) continue;
            if (rr[8] != total) continue;
            int idx = rr[9];
            if (idx >= 0 && idx < total && chunks[idx] == null)
            {
                chunks[idx] = new byte[rr.Length - 12];
                Array.Copy(rr, 12, chunks[idx], 0, chunks[idx].Length);
                received++;
            }
        }
        if (received < total) return null;

        var ms = new MemoryStream();
        for (int i = 0; i < total; i++)
        {
            byte[] ch = chunks[i];
            if (ch == null) return null;
            int offset = (i > 0 && ch.Length >= 4 && ch[0] == 0xFF && ch[1] == 0xFF && ch[2] == 0xFF && ch[3] == 0xFF) ? 4 : 0;
            ms.Write(ch, offset, ch.Length - offset);
        }
        return ms.ToArray();
    }

    static bool A2SQuery(string host, int port, int timeoutMs, out string name, out int players, out int maxPlayers)
    {
        name = "";
        players = 0;
        maxPlayers = 0;
        try
        {
            var ep = ResolveEndPoint(host, port, timeoutMs);
            if (ep == null) return false;
            using (var udp = new UdpClient())
            {
                udp.Client.ReceiveTimeout = timeoutMs;
                byte[] req = A2SReq(null);
                udp.Send(req, req.Length, ep);
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] resp = udp.Receive(ref from);
                if (!SameEndPoint(from, ep)) return false;

                int tries = 0;
                while (resp != null && resp.Length >= 9 && IsSimpleHeader(resp, 0x41) && tries < 2)
                {
                    tries++;
                    byte[] req2 = A2SReq(new byte[] { resp[5], resp[6], resp[7], resp[8] });
                    udp.Send(req2, req2.Length, ep);
                    from = new IPEndPoint(IPAddress.Any, 0);
                    resp = udp.Receive(ref from);
                    if (!SameEndPoint(from, ep)) return false;
                }

                resp = ReceiveA2SPayload(udp, ep, resp);
                if (resp == null || resp.Length < 6 || !IsSimpleHeader(resp, 0x49)) return false;
                int p = 6;
                int nend = p;
                while (nend < resp.Length && resp[nend] != 0) nend++;
                if (nend >= resp.Length) return false;
                name = Encoding.UTF8.GetString(resp, p, Math.Max(0, nend - p));
                p = nend + 1;

                for (int i = 0; i < 3; i++)
                {
                    while (p < resp.Length && resp[p] != 0) p++;
                    if (p >= resp.Length) return false;
                    p++;
                }

                p += 2; // AppID
                if (p + 1 >= resp.Length) return false;
                players = resp[p];
                maxPlayers = resp[p + 1];
                return true;
            }
        }
        catch { return false; }
    }

    static string AddrOf(string entry)
    {
        if (string.IsNullOrEmpty(entry)) return "";
        int bar = entry.IndexOf('|');
        return (bar >= 0 ? entry.Substring(0, bar) : entry).Trim();
    }

    int laidW = -1, laidH = -1;

    void LayoutJoin()
    {
        if (pJoinPanel == null) return;
        int W = pJoinPanel.ClientSize.Width;
        int H = pJoinPanel.ClientSize.Height;
        try
        {
            if (tabs != null && pJoinPanel.Parent != null)
            {
                int cap = tabs.DisplayRectangle.Height - pJoinPanel.Top - 6;
                if (cap > 200 && cap < H) H = cap;
            }
        }
        catch { }
        laidW = W;
        laidH = H;
        if (W < 360) W = 360;
        if (H < 260) H = 260;

        int bx = W - 8;
        if (btnRefreshFav != null)
        {
            btnRefreshFav.SetBounds(bx - 104, 46, 104, 28);
            bx -= 114;
        }
        if (btnUncheckAll != null)
        {
            btnUncheckAll.SetBounds(bx - 74, 46, 74, 28);
            bx -= 80;
        }
        if (btnCheckAll != null)
        {
            btnCheckAll.SetBounds(bx - 64, 46, 64, 28);
            bx -= 72;
        }
        if (lblListHint != null) lblListHint.SetBounds(8, 52, Math.Max(120, bx - 12), 22);

        int pageW = W + 16;
        LayoutToolBar();

        int hintH = 42;
        int hintY = H - 6 - hintH;
        int ipY = hintY - 36;
        if (ipY < 160) ipY = 160;

        if (lblJoinHint != null) lblJoinHint.SetBounds(8, hintY, W - 16, hintH);
        if (lblNewIp != null) lblNewIp.SetBounds(8, ipY + 6, 54, 22);
        if (txtNewIp != null) txtNewIp.SetBounds(64, ipY + 2, 176, 26);
        if (lblNewAlias != null) lblNewAlias.SetBounds(248, ipY + 6, 40, 22);
        if (txtNewAlias != null) txtNewAlias.SetBounds(290, ipY + 2, 96, 26);
        if (btnAddFav != null) btnAddFav.SetBounds(394, ipY, 104, 30);
        if (btnPasteFav != null) btnPasteFav.SetBounds(504, ipY, 84, 30);
        if (btnEditFav != null) btnEditFav.SetBounds(594, ipY, 76, 30);
        if (btnDelFav != null) btnDelFav.SetBounds(676, ipY, 88, 30);

        int listBottom = (JoinModeIdx == 0) ? (hintY - 8) : (ipY - 10);
        int listH = Math.Max(80, listBottom - 80);
        if (lblTapGuide != null) lblTapGuide.SetBounds(8, 80, W - 16, listH);
        if (lvSrv != null) lvSrv.SetBounds(8, 80, W - 16, listH);
        if (lvSrv != null && lvSrv.Columns.Count == 4)
        {
            int wAddr = 156, wPl = 104, wAlias = 106;
            int wReal = W - 16 - wAddr - wPl - wAlias - 26;
            if (wReal < 160) wReal = 160;
            lvSrv.Columns[0].Width = wAlias;
            lvSrv.Columns[1].Width = wReal;
            lvSrv.Columns[2].Width = wPl;
            lvSrv.Columns[3].Width = wAddr;
        }
    }

    int SelectedFavIndex()
    {
        if (lvSrv == null || lvSrv.SelectedIndices.Count == 0) return -1;
        if (lvSrv.FocusedItem != null && lvSrv.FocusedItem.Selected) return lvSrv.FocusedItem.Index;
        return lvSrv.SelectedIndices[0];
    }

    List<int> SelectedIndexList()
    {
        var list = new List<int>();
        if (lvSrv == null) return list;
        foreach (ListViewItem it in lvSrv.SelectedItems) list.Add(it.Index);
        list.Sort();
        return list;
    }

    string SelectedAddr()
    {
        int i = SelectedFavIndex();
        if (i < 0 || i >= lvSrv.Items.Count) return "";
        return Convert.ToString(lvSrv.Items[i].Tag);
    }

    void FillSrvList() { FillSrvList(null); }

    void FillSrvList(string selectAddr)
    {
        if (lvSrv == null) return;

        Interlocked.Increment(ref favVersion);
        favPending = 0;
        favDirty = false;
        if (btnRefreshFav != null) { btnRefreshFav.Enabled = true; btnRefreshFav.Text = "↻ 刷新人数"; }

        var checks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        string oldSel = SelectedAddr();
        for (int i = 0; i < lvSrv.Items.Count; i++)
        {
            string t = Convert.ToString(lvSrv.Items[i].Tag);
            if (t != null) checks[t] = lvSrv.Items[i].Checked;
        }
        if (string.IsNullOrEmpty(selectAddr)) selectAddr = oldSel;

        suppressListCheck = true;
        lvSrv.BeginUpdate();
        lvSrv.Items.Clear();
        favCount = new int[favList.Length];
        favMax = new int[favList.Length];
        favName = new string[favList.Length];
        for (int i = 0; i < favList.Length; i++)
        {
            favCount[i] = -2;      // -2 = 还没查过（显示 —），-1 = 查询失败
            favMax[i] = 0;
            favName[i] = "";
            string addr = AddrOf(favList[i]);
            string[] parts = favList[i].Split('|');
            string nm = (parts.Length > 1) ? parts[1].Trim() : "";
            string cached = (parts.Length > 2) ? parts[2].Trim() : "";
            if (cached.Length > 0) favName[i] = cached;

            var it = new ListViewItem(nm);
            it.UseItemStyleForSubItems = false;
            it.SubItems.Add(cached.Length > 0 ? cached : "—");
            it.SubItems.Add("—");
            it.SubItems.Add(addr);
            it.Tag = addr;
            bool want;
            if (chkPrefAuthoritative) want = chkPref.Contains(addr);   // 刚读完 ini：以文件为准
            else if (checks.ContainsKey(addr)) want = checks[addr];    // 运行中重建：保持现状
            else if (chkPrefLoaded) want = chkPref.Contains(addr);
            else want = true;                                          // 从没存过 → 默认全选
            it.Checked = want;
            lvSrv.Items.Add(it);
            UpdateSrvRow(i);
        }
        lvSrv.EndUpdate();
        suppressListCheck = false;
        chkPrefAuthoritative = false;

        int sel = -1;
        for (int i = 0; i < lvSrv.Items.Count; i++)
            if (string.Equals(Convert.ToString(lvSrv.Items[i].Tag), selectAddr, StringComparison.OrdinalIgnoreCase)) { sel = i; break; }
        if (sel < 0 && lvSrv.Items.Count > 0) sel = 0;
        if (sel >= 0)
        {
            lvSrv.Items[sel].Selected = true;
            lvSrv.Items[sel].Focused = true;
            lvSrv.EnsureVisible(sel);
            pendingSelAddr = Convert.ToString(lvSrv.Items[sel].Tag);
        }
        UpdateJoinHint();
    }

    void UpdateSrvRow(int i)
    {
        if (lvSrv == null || i < 0 || i >= lvSrv.Items.Count || i >= favCount.Length || i >= favList.Length) return;
        ListViewItem it = lvSrv.Items[i];
        it.UseItemStyleForSubItems = false;

        string[] parts = favList[i].Split('|');
        string alias = (parts.Length > 1) ? parts[1].Trim() : "";
        string addr = parts[0].Trim();
        bool isCurrentTarget = typingOn && autoOn && !string.IsNullOrEmpty(currentTargetAddr)
            && string.Equals(addr, currentTargetAddr, StringComparison.OrdinalIgnoreCase);

        string col0 = isCurrentTarget ? ("▶ " + (alias.Length > 0 ? alias : "挤服中")) : alias;
        if (it.SubItems[0].Text != col0) it.SubItems[0].Text = col0;
        Color rowBg = IsDark ? Color.FromArgb(13, 17, 23) : Color.White;
        it.BackColor = rowBg;
        it.SubItems[0].BackColor = rowBg;
        it.SubItems[1].BackColor = rowBg;
        it.SubItems[3].BackColor = rowBg;
        it.SubItems[0].ForeColor = isCurrentTarget
            ? (IsDark ? Color.FromArgb(88, 166, 255) : Color.FromArgb(24, 64, 128))
            : (IsDark ? Color.FromArgb(230, 237, 243) : Color.FromArgb(30, 41, 59));
        it.SubItems[0].Font = TrackFont("Microsoft YaHei UI", 9f, isCurrentTarget ? FontStyle.Bold : FontStyle.Regular);

        string rname = (i < favName.Length && favName[i] != null) ? favName[i].Trim() : "";
        if (rname.Length == 0) rname = "—";
        if (it.SubItems[1].Text != rname) it.SubItems[1].Text = rname;
        it.SubItems[1].ForeColor = IsDark ? Color.FromArgb(201, 209, 217) : Color.FromArgb(51, 65, 85);

        string txt;
        Color fg;
        Color bg = rowBg;
        bool boldCount = false;
        if (favCount[i] == -3)
        {
            txt = "查询中…";
            fg = IsDark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(100, 116, 139);
        }
        else if (favCount[i] == -2)
        {
            txt = "—";
            fg = IsDark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(140, 148, 160);
        }
        else if (favCount[i] < 0)
        {
            txt = "超时/失败";
            fg = IsDark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(140, 148, 160);
        }
        else
        {
            bool full = favMax[i] > 0 && favCount[i] >= favMax[i];
            txt = favCount[i] + "/" + favMax[i] + (full ? " [满]" : " [空]");
            if (IsDark)
            {
                fg = full ? Color.FromArgb(255, 161, 152) : Color.FromArgb(126, 231, 135);
                bg = full ? Color.FromArgb(68, 28, 34) : Color.FromArgb(22, 58, 38);
            }
            else
            {
                fg = full ? Color.FromArgb(185, 28, 28) : Color.FromArgb(21, 128, 61);
                bg = full ? Color.FromArgb(254, 226, 226) : Color.FromArgb(220, 252, 231);
            }
            boldCount = true;
        }
        if (it.SubItems[2].Text != txt) it.SubItems[2].Text = txt;
        it.SubItems[2].ForeColor = fg;
        it.SubItems[2].BackColor = bg;
        it.SubItems[2].Font = TrackFont("Microsoft YaHei UI", 9f, boldCount ? FontStyle.Bold : FontStyle.Regular);

        it.SubItems[3].ForeColor = IsDark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(100, 116, 139);
        if (lvSrv.IsHandleCreated) lvSrv.Invalidate(it.Bounds);
    }

    void RefreshActiveTargetRowHighlight()
    {
        if (lvSrv == null) return;
        for (int i = 0; i < lvSrv.Items.Count && i < favList.Length; i++)
            UpdateSrvRow(i);
    }

    void SortSrvListByColumn(int col)
    {
        if (lvSrv == null || favList.Length <= 1 || favPending > 0) return;
        if (srvSortCol == col) srvSortAsc = !srvSortAsc;
        else { srvSortCol = col; srvSortAsc = true; }

        int n = favList.Length;
        var checks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lvSrv.Items.Count; i++)
        {
            string a = Convert.ToString(lvSrv.Items[i].Tag);
            if (a != null) checks[a] = lvSrv.Items[i].Checked;
        }
        string selAddr = SelectedAddr();

        int[] order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;

        Array.Sort(order, delegate(int a, int b)
        {
            int cmp = 0;
            if (col == 0)
            {
                string[] pa = favList[a].Split('|');
                string[] pb = favList[b].Split('|');
                string aa = pa.Length > 1 ? pa[1] : "";
                string ab = pb.Length > 1 ? pb[1] : "";
                cmp = string.Compare(aa, ab, StringComparison.CurrentCultureIgnoreCase);
            }
            else if (col == 1)
            {
                string na = a < favName.Length ? (favName[a] ?? "") : "";
                string nb = b < favName.Length ? (favName[b] ?? "") : "";
                cmp = string.Compare(na, nb, StringComparison.CurrentCultureIgnoreCase);
            }
            else if (col == 2)
            {
                // 按空位优先级排序：有空位(0) < 满员(1) < 未查/失败(2)
                int rankA = (favCount[a] >= 0 && favMax[a] > 0) ? (favCount[a] < favMax[a] ? 0 : 1) : 2;
                int rankB = (favCount[b] >= 0 && favMax[b] > 0) ? (favCount[b] < favMax[b] ? 0 : 1) : 2;
                if (rankA != rankB) cmp = rankA - rankB;
                else cmp = favCount[b] - favCount[a];
            }
            else
            {
                cmp = string.Compare(AddrOf(favList[a]), AddrOf(favList[b]), StringComparison.OrdinalIgnoreCase);
            }
            return srvSortAsc ? cmp : -cmp;
        });

        string[] newFav = new string[n];
        int[] newCnt = new int[n];
        int[] newMax = new int[n];
        string[] newNam = new string[n];
        for (int i = 0; i < n; i++)
        {
            int old = order[i];
            newFav[i] = favList[old];
            newCnt[i] = old < favCount.Length ? favCount[old] : -2;
            newMax[i] = old < favMax.Length ? favMax[old] : 0;
            newNam[i] = old < favName.Length ? favName[old] : "";
        }
        favList = newFav;
        SaveFavs();

        suppressListCheck = true;
        lvSrv.BeginUpdate();
        lvSrv.Items.Clear();
        favCount = newCnt;
        favMax = newMax;
        favName = newNam;
        for (int i = 0; i < n; i++)
        {
            string addr = AddrOf(favList[i]);
            string[] parts = favList[i].Split('|');
            string nm = (parts.Length > 1) ? parts[1].Trim() : "";
            string cached = favName[i] ?? "";
            var it = new ListViewItem(nm);
            it.UseItemStyleForSubItems = false;
            it.SubItems.Add(cached.Length > 0 ? cached : "—");
            it.SubItems.Add("—");
            it.SubItems.Add(addr);
            it.Tag = addr;
            it.Checked = !checks.ContainsKey(addr) || checks[addr];
            lvSrv.Items.Add(it);
            UpdateSrvRow(i);
        }
        lvSrv.EndUpdate();
        suppressListCheck = false;

        for (int i = 0; i < lvSrv.Items.Count; i++)
        {
            if (string.Equals(Convert.ToString(lvSrv.Items[i].Tag), selAddr, StringComparison.OrdinalIgnoreCase))
            {
                lvSrv.Items[i].Selected = true;
                lvSrv.Items[i].Focused = true;
                break;
            }
        }
        SyncActiveTargets();
        UpdateJoinHint();
    }

    void UpdateJoinHint()
    {
        if (lblJoinHint == null) return;
        string a = SelectedAddr();
        string selTxt = (a.Length == 0) ? "（未选中）" : a;
        int ix = SelectedFavIndex();
        if (ix >= 0 && ix < favCount.Length && favCount[ix] >= 0)
        {
            bool full = favMax[ix] > 0 && favCount[ix] >= favMax[ix];
            selTxt += string.Format("　在线 {0}/{1} {2}", favCount[ix], favMax[ix], full ? "[满员]" : "[有空位]");
        }
        int nchk = 0;
        if (lvSrv != null) for (int i = 0; i < lvSrv.Items.Count; i++) if (lvSrv.Items[i].Checked) nchk++;
        int total = (lvSrv == null) ? 0 : lvSrv.Items.Count;
        string mode;
        int m = JoinModeIdx;
        if (m == 0) mode = "手动档：在游戏内点击连接后，按 " + KeyName(2) + " 自动连按回车";
        else if (m == 2) mode = "收藏轮询：在勾选的服务器之间轮流发送 connect，满员时自动切换下一台";
        else mode = "单一 IP：只重试列表中选中的这台服务器";
        int nsel = (lvSrv == null) ? 0 : lvSrv.SelectedItems.Count;
        string multi = (nsel > 1) ? ("（已选 " + nsel + " 行，单一 IP 将使用当前焦点行）") : "";
        lblJoinHint.Text = string.Format(
            "单一 IP 目标：{0}{1}　｜　参与轮询：已勾选 {2} / {3} 个\r\n{4}",
            selTxt, multi, nchk, total, mode);
        if (lblListHint != null && total == 0 && m != 0)
            lblListHint.Text = "列表为空：请在下方填写 IP:端口 后点击“★ 加入收藏”，或复制地址后点击“粘贴加服”";
    }

    void SetAllChecks(bool on)
    {
        if (lvSrv == null) return;
        suppressListCheck = true;
        lvSrv.BeginUpdate();
        for (int i = 0; i < lvSrv.Items.Count; i++) lvSrv.Items[i].Checked = on;
        lvSrv.EndUpdate();
        suppressListCheck = false;
        SyncActiveTargets();
        UpdateJoinHint();
        RefreshUi(false);
    }

    int favPending = 0;
    // 启动首刷的补查：只对失败的服再查一次，手动刷新不补
    bool bootRefresh = false;
    System.Windows.Forms.Timer retryTimer;
    System.Collections.Generic.List<int> retryList = new System.Collections.Generic.List<int>();
    int retryPos = 0;
    bool favDirty = false;

    void RefreshAllFavs()
    {
        if (lvSrv == null) return;
        if (favList.Length == 0)
        {
            toast.ShowText("列表是空的，先加一个服务器");
            return;
        }
        if (favPending > 0) return;

        int myVer = Interlocked.Increment(ref favVersion);
        favPending = favList.Length;
        favDirty = false;
        if (btnRefreshFav != null) { btnRefreshFav.Enabled = false; btnRefreshFav.Text = "刷新中…"; }
        for (int i = 0; i < favList.Length && i < lvSrv.Items.Count; i++)
        {
            favCount[i] = -3;
            UpdateSrvRow(i);
        }
        UpdateJoinHint();

        for (int i = 0; i < favList.Length; i++)
        {
            int fi = i;
            string addrQ = AddrOf(favList[i]);
            int colon = addrQ.LastIndexOf(':');
            int pn2;
            if (colon <= 0 || !int.TryParse(addrQ.Substring(colon + 1), out pn2))
            {
                DoneOneFav(myVer, fi, addrQ, false, 0, 0, "");
                continue;
            }
            string host2 = addrQ.Substring(0, colon).Trim();
            Task.Run(delegate
            {
                string nm2 = "";
                int p3 = 0, m3 = 0;
                bool ok2 = false;
                try { ok2 = A2SQuery(host2, pn2, 1400, out nm2, out p3, out m3); }
                catch { ok2 = false; }
                int pv = ok2 ? p3 : 0, mv = ok2 ? m3 : 0;
                bool okv = ok2;
                string nmv = ok2 ? nm2 : "";
                SafeBeginInvoke(delegate { DoneOneFav(myVer, fi, addrQ, okv, pv, mv, nmv); });
            });
        }
    }

    // 启动首刷后，把失败的服再串行补查一遍（间隔 350ms、超时 2.5s），避免并发丢包
    void StartRetryFailedFavs()
    {
        retryList.Clear();
        for (int i = 0; i < favCount.Length; i++)
            if (favCount[i] < 0) retryList.Add(i);
        if (retryList.Count == 0) return;
        Native.Log("startup retry: " + retryList.Count + " failed servers");
        retryPos = 0;
        if (retryTimer == null)
        {
            retryTimer = new System.Windows.Forms.Timer(uiComponents);
            retryTimer.Interval = 350;
            retryTimer.Tick += delegate { RetryNextFav(); };
        }
        retryTimer.Start();
    }

    void RetryNextFav()
    {
        if (isClosing || retryPos >= retryList.Count)
        {
            if (retryTimer != null) retryTimer.Stop();
            Native.Log("startup retry done");
            return;
        }
        int i = retryList[retryPos++];
        if (i < 0 || i >= favList.Length || i >= favCount.Length) return;
        string addrQ = AddrOf(favList[i]);
        int colon = addrQ.LastIndexOf(':');
        int pn2;
        if (colon <= 0 || !int.TryParse(addrQ.Substring(colon + 1), out pn2)) return;
        string host2 = addrQ.Substring(0, colon).Trim();
        int fi = i;
        int myVer = favVersion;
        Task.Run(delegate
        {
            string nm2 = "";
            int p3 = 0, m3 = 0;
            bool ok2 = false;
            try { ok2 = A2SQuery(host2, pn2, 2500, out nm2, out p3, out m3); }
            catch { ok2 = false; }
            if (!ok2) return;                       // 还失败就保留「超时/失败」
            int pv = p3, mv = m3;
            string nmv = nm2;
            SafeBeginInvoke(delegate
            {
                if (myVer != favVersion) return;
                if (fi < 0 || fi >= favCount.Length) return;
                favCount[fi] = pv;
                favMax[fi] = mv;
                if (!string.IsNullOrEmpty(nmv) && fi < favName.Length) favName[fi] = nmv;
                if (CacheSrvName(fi, nmv)) SaveFavs();
                UpdateSrvRow(fi);
                UpdateJoinHint();
                Native.Log("startup retry ok: " + addrQ + " " + pv + "/" + mv);
            });
        });
    }

    void QueryFavOne(int i)
    {
        if (i < 0 || i >= favList.Length || i >= favCount.Length) return;
        int myVer = favVersion;
        string addrQ = AddrOf(favList[i]);
        int colon = addrQ.LastIndexOf(':');
        int pn2;
        if (colon <= 0 || !int.TryParse(addrQ.Substring(colon + 1), out pn2))
        {
            favCount[i] = -1;
            UpdateSrvRow(i);
            return;
        }
        string host2 = addrQ.Substring(0, colon).Trim();
        int fi = i;
        favCount[fi] = -3;
        UpdateSrvRow(fi);

        Task.Run(delegate
        {
            string nm2 = "";
            int p3 = 0, m3 = 0;
            bool ok2 = false;
            try { ok2 = A2SQuery(host2, pn2, 850, out nm2, out p3, out m3); }
            catch { ok2 = false; }
            int pv = ok2 ? p3 : 0, mv = ok2 ? m3 : 0;
            bool okv = ok2;
            SafeBeginInvoke(delegate
            {
                if (myVer != favVersion || fi >= favCount.Length || fi >= favList.Length) return;
                if (!string.Equals(AddrOf(favList[fi]), addrQ, StringComparison.OrdinalIgnoreCase)) return;
                favCount[fi] = okv ? pv : -1;
                favMax[fi] = okv ? mv : 0;
                if (okv && fi < favName.Length && !string.IsNullOrEmpty(nm2)) favName[fi] = nm2;
                if (okv && CacheSrvName(fi, nm2)) SaveFavs();
                UpdateSrvRow(fi);
                if (fi == SelectedFavIndex()) UpdateJoinHint();
            });
        });
    }

    bool CacheSrvName(int i, string name)
    {
        if (i < 0 || i >= favList.Length) return false;
        name = (name == null) ? "" : CleanCtrl(name.Replace('|', ' ').Trim(), true);
        if (name.Length == 0) return false;
        string[] parts = favList[i].Split('|');
        string cur = (parts.Length > 2) ? parts[2].Trim() : "";
        if (cur == name) return false;
        string alias = (parts.Length > 1) ? parts[1].Trim() : "";
        string addr = parts[0].Trim();
        favList[i] = addr + "|" + alias + "|" + name;
        return true;
    }

    void DoneOneFav(int ver, int fi, string expectedAddr, bool ok, int players, int maxP, string srvName)
    {
        if (ver != favVersion) return;
        if (fi >= 0 && fi < favCount.Length && fi < favList.Length
            && string.Equals(AddrOf(favList[fi]), expectedAddr, StringComparison.OrdinalIgnoreCase))
        {
            favCount[fi] = ok ? players : -1;
            favMax[fi] = ok ? maxP : 0;
            if (ok && fi < favName.Length && !string.IsNullOrEmpty(srvName)) favName[fi] = srvName;
            if (ok && CacheSrvName(fi, srvName)) favDirty = true;
            UpdateSrvRow(fi);
        }
        if (favPending > 0) favPending--;
        if (favPending <= 0)
        {
            favPending = 0;
            if (bootRefresh)
            {
                bootRefresh = false;
                StartRetryFailedFavs();
            }
            if (favDirty) { favDirty = false; SaveFavs(); }
            if (btnRefreshFav != null) { btnRefreshFav.Enabled = true; btnRefreshFav.Text = "↻ 刷新人数"; }
            int openSlots = 0, okTotal = 0;
            for (int k = 0; k < favCount.Length; k++)
            {
                if (favCount[k] >= 0)
                {
                    okTotal++;
                    if (favMax[k] > 0 && favCount[k] < favMax[k]) openSlots++;
                }
            }
            if (okTotal > 0)
                toast.ShowText("刷新完成：" + openSlots + " 个服有空位（共响应 " + okTotal + "/" + favCount.Length + "）");
            UpdateJoinHint();
            RefreshUi(true);
        }
    }

    void AddFavEntry(string addr, string aliasInput, string srvRealName)
    {
        addr = (addr == null) ? "" : addr.Trim();
        if (addr.Length == 0) return;
        for (int i = 0; i < favList.Length; i++)
            if (string.Equals(AddrOf(favList[i]), addr, StringComparison.OrdinalIgnoreCase)) return;

        string alias = (aliasInput == null) ? "" : CleanCtrl(aliasInput.Replace('|', ' ').Trim(), true);
        string real = (srvRealName == null) ? "" : CleanCtrl(srvRealName.Replace('|', ' ').Trim(), true);
        var nf = new string[favList.Length + 1];
        Array.Copy(favList, nf, favList.Length);
        nf[nf.Length - 1] = addr + "|" + alias + "|" + real;
        favList = nf;
        SaveFavs();
        FillSrvList(addr);
        Native.Log("fav add: " + addr + " alias=" + alias + " real=" + real);
        toast.ShowText("已加入收藏：" + (alias.Length > 0 ? alias : (real.Length > 0 ? real : addr)));
        QueryFavOne(SelectedFavIndex());
    }

    void PasteAndAddFav()
    {
        try
        {
            string clip = Clipboard.ContainsText() ? (Clipboard.GetText() ?? "").Trim() : "";
            if (clip.Length == 0)
            {
                toast.ShowText("剪贴板为空，请先复制服务器 IP:端口");
                return;
            }
            // 支持从一段带字的群消息里提取 IP:端口
            Match m = Regex.Match(clip, @"([a-zA-Z0-9\.\-]+:\d{2,5})");
            string candidate = m.Success ? m.Groups[1].Value : clip;
            string host;
            int port;
            if (!ParseHostPort(candidate, out host, out port))
            {
                toast.ShowText("剪贴板里没找到有效的 IP:端口");
                return;
            }
            if (txtNewIp != null) txtNewIp.Text = host + ":" + port;
            AddFavFromBox();
        }
        catch (Exception ex)
        {
            toast.ShowText("读取剪贴板失败：" + ex.Message);
        }
    }

    void AddFavFromBox()
    {
        string ip = (txtNewIp == null) ? "" : txtNewIp.Text.Trim();
        string alias = (txtNewAlias == null) ? "" : txtNewAlias.Text.Trim();
        string host;
        int port;
        if (!ParseHostPort(ip, out host, out port))
        {
            toast.ShowText("格式写成 IP:端口，例如 127.0.0.1:27015");
            return;
        }
        string addr = host + ":" + port;
        for (int i = 0; i < favList.Length; i++)
            if (string.Equals(AddrOf(favList[i]), addr, StringComparison.OrdinalIgnoreCase))
            {
                toast.ShowText("这个地址已经在列表里了");
                return;
            }

        toast.ShowText("正在查询服务器信息…");
        string h = host;
        int p = port;
        string al = alias;
        Task.Run(delegate
        {
            string nm = null;
            try
            {
                string n0;
                int pc, mc;
                if (A2SQuery(h, p, 900, out n0, out pc, out mc)) nm = n0;
            }
            catch { }
            SafeBeginInvoke(delegate
            {
                AddFavEntry(h + ":" + p, al, nm);
                if (txtNewIp != null) txtNewIp.Text = "";
                if (txtNewAlias != null) txtNewAlias.Text = "";
            });
        });
    }

    void DeleteSelectedFav()
    {
        if (lvSrv == null) return;
        var idxs = SelectedIndexList();
        if (idxs.Count == 0)
        {
            toast.ShowText("先在列表里选中要删除的服务器");
            return;
        }
        string what = (idxs.Count == 1) ? AddrOf(favList[idxs[0]]) : (idxs.Count + " 个服务器");
        if (MessageBox.Show(this, "从收藏里删除 " + what + " ？\r\n\r\n（只删本地列表，不影响服务器）",
                "RX 工具箱", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        var keep = new List<string>();
        for (int i = 0; i < favList.Length; i++)
            if (!idxs.Contains(i)) keep.Add(favList[i]);
        favList = keep.ToArray();
        SaveFavs();
        FillSrvList();
        SyncActiveTargets();
        Native.Log("fav del " + idxs.Count + " rows");
        toast.ShowText("已删除 " + what);
    }

    static bool ParseHostPort(string raw, out string host, out int port)
    {
        host = "";
        port = 0;
        if (raw == null) return false;
        string ip = raw.Trim();
        if (ip.StartsWith("steam://connect/", StringComparison.OrdinalIgnoreCase))
            ip = ip.Substring(16).Trim();
        if (ip.StartsWith("connect ", StringComparison.OrdinalIgnoreCase))
            ip = ip.Substring(8).Trim();
        ip = ip.TrimEnd('/');
        int c = ip.LastIndexOf(':');
        string h = (c > 0) ? ip.Substring(0, c).Trim() : ip;
        string pt = (c > 0) ? ip.Substring(c + 1).Trim() : "27015";
        int pv;
        if (h.Length == 0 || h.Length > 253 || !int.TryParse(pt, out pv) || pv <= 0 || pv > 65535) return false;
        for (int i = 0; i < h.Length; i++)
        {
            char ch = h[i];
            bool okCh = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z')
                     || (ch >= 'A' && ch <= 'Z') || ch == '.' || ch == '-';
            if (!okCh) return false;
        }
        host = h;
        port = pv;
        return true;
    }

    void EditSelectedFav()
    {
        int ix = SelectedFavIndex();
        if (ix < 0 || ix >= favList.Length)
        {
            toast.ShowText("先在列表里选中要编辑的服务器");
            return;
        }
        ShowEditFav(ix);
    }

    void ShowEditFav(int ix)
    {
        if (ix < 0 || ix >= favList.Length) return;
        string[] parts = favList[ix].Split('|');
        string oldAddr = parts[0].Trim();
        string oldAlias = (parts.Length > 1) ? parts[1].Trim() : "";
        string oldName = (parts.Length > 2) ? parts[2].Trim() : "";

        DialogResult dr;
        string alias;
        string rawIp;
        using (var f = new Form())
        {
            f.Text = "编辑收藏 · " + oldAddr;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.MinimizeBox = false;
            f.MaximizeBox = false;
            f.ShowInTaskbar = false;
            f.ClientSize = new Size(470, 240);
            f.BackColor = Color.White;
            f.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);

            var l1 = new Label();
            l1.Text = "备注名（列表里显示的短名字，留空则清空备注）";
            l1.ForeColor = Color.FromArgb(70, 80, 95);
            l1.SetBounds(14, 12, 440, 20);
            f.Controls.Add(l1);

            var tAlias = new TextBox();
            tAlias.SetBounds(14, 34, 440, 26);
            tAlias.Text = oldAlias;
            f.Controls.Add(tAlias);

            var l2 = new Label();
            l2.Text = "IP:端口（服务器换 IP 就改这里）";
            l2.ForeColor = Color.FromArgb(70, 80, 95);
            l2.SetBounds(14, 72, 440, 20);
            f.Controls.Add(l2);

            var tIp = new TextBox();
            tIp.SetBounds(14, 94, 440, 26);
            tIp.Text = oldAddr;
            f.Controls.Add(tIp);

            var hint = new Label();
            hint.Text = "改 IP 会自动重新探测真名和人数；备注名只影响显示，不影响连接。";
            hint.ForeColor = Color.FromArgb(120, 126, 136);
            hint.SetBounds(14, 128, 440, 40);
            f.Controls.Add(hint);

            var bOk = new Button();
            bOk.Text = "保存";
            bOk.SetBounds(262, 194, 92, 32);
            bOk.DialogResult = DialogResult.OK;
            f.Controls.Add(bOk);

            var bNo = new Button();
            bNo.Text = "取消";
            bNo.SetBounds(366, 194, 88, 32);
            bNo.DialogResult = DialogResult.Cancel;
            f.Controls.Add(bNo);

            f.AcceptButton = bOk;
            f.CancelButton = bNo;
            ThemeDialog(f);
            tAlias.SelectAll();
            tAlias.Focus();

            dr = f.ShowDialog(this);
            alias = CleanCtrl(tAlias.Text.Replace('|', ' ').Trim(), true);
            rawIp = tIp.Text.Trim();
        }
        if (dr != DialogResult.OK) return;

        string newHost;
        int newPort;
        if (!ParseHostPort(rawIp, out newHost, out newPort))
        {
            toast.ShowText("IP 格式不对：写成 IP:端口，例如 1.2.3.4:27015");
            return;
        }
        string newAddr = newHost + ":" + newPort;
        bool addrChanged = !string.Equals(newAddr, oldAddr, StringComparison.OrdinalIgnoreCase);

        if (addrChanged)
        {
            for (int i = 0; i < favList.Length; i++)
            {
                if (i == ix) continue;
                if (string.Equals(AddrOf(favList[i]), newAddr, StringComparison.OrdinalIgnoreCase))
                {
                    toast.ShowText("列表里已经有 " + newAddr + " 了");
                    return;
                }
            }
        }

        favList[ix] = newAddr + "|" + alias + "|" + (addrChanged ? "" : oldName);
        SaveFavs();
        FillSrvList(newAddr);
        SyncActiveTargets();
        Native.Log("fav edit: " + oldAddr + " -> " + favList[ix]);
        if (!addrChanged)
        {
            toast.ShowText(alias.Length > 0 ? ("已保存备注名：" + alias) : "已清空备注名");
            return;
        }
        toast.ShowText("已改到 " + newAddr + "，正在重新探测");
        for (int i = 0; i < favList.Length; i++)
            if (string.Equals(AddrOf(favList[i]), newAddr, StringComparison.OrdinalIgnoreCase)) { QueryFavOne(i); break; }
    }

    void SetChecksOnSelection(bool on)
    {
        if (lvSrv == null) return;
        var idxs = SelectedIndexList();
        if (idxs.Count == 0)
        {
            toast.ShowText("先选中几行");
            return;
        }
        suppressListCheck = true;
        for (int k = 0; k < idxs.Count; k++)
            if (idxs[k] < lvSrv.Items.Count) lvSrv.Items[idxs[k]].Checked = on;
        suppressListCheck = false;
        SyncActiveTargets();
        UpdateJoinHint();
        RefreshUi(false);
    }

    void RefreshSelectedFavs()
    {
        var idxs = SelectedIndexList();
        if (idxs.Count == 0)
        {
            toast.ShowText("先选中几行");
            return;
        }
        for (int k = 0; k < idxs.Count; k++) QueryFavOne(idxs[k]);
        toast.ShowText("正在刷新 " + idxs.Count + " 个服务器的人数…");
    }

}
