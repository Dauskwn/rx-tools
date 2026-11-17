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
    static string Shorten(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
    }

    // ===================== 自定义文本 =====================

    string customPath = "";
    string[] cTitles = new string[0];
    string[] cTexts = new string[0];

    void LoadCustom()
    {
        try
        {
            if (!File.Exists(customPath)) return;
            string raw = File.ReadAllText(customPath, Encoding.UTF8);
            string[] recs = raw.Split('\u0002');
            var ts = new List<string>();
            var cs = new List<string>();
            for (int i = 0; i < recs.Length; i++)
            {
                string rec = recs[i];
                if (rec.Length == 0) continue;
                int p = rec.IndexOf('\u0001');
                if (p < 0) { ts.Add(rec); cs.Add(""); }
                else { ts.Add(rec.Substring(0, p)); cs.Add(rec.Substring(p + 1)); }
            }
            cTitles = ts.ToArray();
            cTexts = cs.ToArray();
        }
        catch (Exception ex) { Native.Log("custom load: " + ex.Message); }
    }

    static string CleanCtrl(string s, bool singleLine)
    {
        if (string.IsNullOrEmpty(s)) return "";
        string res = s.Replace('\u0001', ' ').Replace('\u0002', ' ');
        if (singleLine) res = res.Replace('\r', ' ').Replace('\n', ' ');
        return res;
    }

    static bool AtomicWriteText(string path, string content)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string tmp = path + ".tmp";
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(tmp, content ?? "", Encoding.UTF8);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, null);
                    return true;
                }
                catch
                {
                    File.Copy(tmp, path, true);
                    return true;
                }
            }
            else
            {
                try
                {
                    File.Move(tmp, path);
                    return true;
                }
                catch
                {
                    File.Copy(tmp, path, true);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Native.Log("AtomicWriteText fail (" + path + "): " + ex.Message);
            return false;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    bool SaveCustom()
    {
        try
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cTitles.Length; i++)
                sb.Append(CleanCtrl(cTitles[i], true)).Append('\u0001').Append(CleanCtrl(cTexts[i], false)).Append('\u0002');
            return AtomicWriteText(customPath, sb.ToString());
        }
        catch (Exception ex)
        {
            Native.Log("custom save: " + ex.Message);
            return false;
        }
    }

    void RebuildCustomPage()
    {
        if (tabs == null || customPage == null) return;
        try
        {
            int idx = tabs.TabPages.IndexOf(customPage);
            if (idx < 0) return;
            int prevSel = tabs.SelectedIndex;
            tabs.TabPages.Remove(customPage);
            ForgetOrigColors(customPage);
            customPage.Dispose();
            customPage = BuildCustomPage();
            tabs.TabPages.Insert(idx, customPage);
            ThemeControlTree(customPage);
            if (prevSel >= 0 && prevSel < tabs.TabPages.Count) tabs.SelectedIndex = prevSel;
        }
        catch (Exception ex) { Native.Log("rebuild custom page fail: " + ex.Message); }
    }

    TabPage BuildCustomPage()
    {
        Font ui = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
        Font mono = TrackFont("Consolas", 10f, FontStyle.Regular);
        Font btnBold = TrackFont("Microsoft YaHei UI", 10f, FontStyle.Bold);
        Font cardFont = TrackFont("Microsoft YaHei UI", 10.5f, FontStyle.Bold);

        var page = new TabPage("自定义");
        page.Padding = new Padding(0);
        page.BackColor = Color.White;

        var pCards = new Panel();
        pCards.SetBounds(0, 0, page.ClientSize.Width, page.ClientSize.Height);
        pCards.Dock = DockStyle.Fill;
        page.Controls.Add(pCards);

        var pEdit = new Panel();
        pEdit.SetBounds(0, 0, page.ClientSize.Width, page.ClientSize.Height);
        pEdit.Dock = DockStyle.Fill;
        pEdit.Visible = false;
        page.Controls.Add(pEdit);

        var bEdit = new Button();
        bEdit.Text = "⚙ 编辑条目";
        bEdit.Font = btnBold;
        bEdit.SetBounds(10, 6, 140, 34);
        pCards.Controls.Add(bEdit);

        var tip = new Label();
        tip.Text = "左键单击卡片 = 复制全文，右键卡片 = 快捷编辑/删除　·　上方预览框可划选复制局部片段";
        tip.ForeColor = Color.DimGray;
        tip.SetBounds(160, 12, 520, 22);
        pCards.Controls.Add(tip);

        var lblCardFilter = new Label();
        lblCardFilter.Text = "筛选：";
        lblCardFilter.ForeColor = Color.Gray;
        lblCardFilter.SetBounds(686, 12, 44, 22);
        lblCardFilter.Anchor = AnchorStyles.Top;
        pCards.Controls.Add(lblCardFilter);

        var txtCardFilter = new TextBox();
        txtCardFilter.Font = ui;
        txtCardFilter.SetBounds(730, 8, 140, 26);
        txtCardFilter.Anchor = AnchorStyles.Top;
        // 靠右的位置在 Resize 里算，避免 Anchor 在面板还没尺寸时算出屏幕外的坐标
        pCards.Resize += delegate
        {
            int right = pCards.ClientSize.Width - 10;
            txtCardFilter.Left = Math.Max(730, right - txtCardFilter.Width);
            lblCardFilter.Left = txtCardFilter.Left - 44;
        };
        pCards.Controls.Add(txtCardFilter);

        var preview = new TextBox();
        preview.Font = mono;
        preview.Multiline = true;
        preview.ReadOnly = true;
        preview.ScrollBars = ScrollBars.Vertical;
        preview.BorderStyle = BorderStyle.FixedSingle;
        preview.BackColor = Color.FromArgb(250, 252, 255);
        preview.SetBounds(10, 46, pCards.ClientSize.Width - 20, 84);
        preview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pCards.Controls.Add(preview);

        var flow = new FlowLayoutPanel();
        flow.SetBounds(10, 138, pCards.ClientSize.Width - 20, pCards.ClientSize.Height - 146);
        flow.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        flow.AutoScroll = true;
        flow.FlowDirection = FlowDirection.LeftToRight;
        flow.WrapContents = true;
        flow.Padding = new Padding(0, 4, 0, 0);
        pCards.Controls.Add(flow);

        var hover = new ToolTip(uiComponents);
        hover.AutoPopDelay = 20000;
        hover.InitialDelay = 250;
        hover.ReshowDelay = 80;
        hover.ShowAlways = true;

        var bBack = new Button();
        bBack.Text = "← 返回卡片页";
        bBack.Font = btnBold;
        bBack.SetBounds(10, 6, 136, 34);
        pEdit.Controls.Add(bBack);

        var tip2 = new Label();
        tip2.Text = "选中左侧条目后可在右侧修改名称和内容（切换条目或返回时会自动保存修改）";
        tip2.ForeColor = Color.DimGray;
        tip2.SetBounds(156, 12, 680, 22);
        pEdit.Controls.Add(tip2);

        var gL = new GroupBox();
        gL.Text = "我的文本列表";
        gL.Font = ui;
        gL.SetBounds(10, 46, 380, pEdit.ClientSize.Height - 56);
        gL.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        pEdit.Controls.Add(gL);

        var list = new BufferedListBox();
        list.Font = TrackFont("Microsoft YaHei UI", 10f, FontStyle.Regular);
        list.BorderStyle = BorderStyle.FixedSingle;
        list.IntegralHeight = false;
        list.SetBounds(12, 24, 356, 200);
        gL.Controls.Add(list);

        var bAdd = new Button(); bAdd.Text = "新增";
        var bDel = new Button(); bDel.Text = "删除";
        var bUp = new Button(); bUp.Text = "上移";
        var bDown = new Button(); bDown.Text = "下移";
        gL.Controls.Add(bAdd); gL.Controls.Add(bDel); gL.Controls.Add(bUp); gL.Controls.Add(bDown);

        var gR = new GroupBox();
        gR.Text = "编辑选中条目";
        gR.Font = ui;
        gR.SetBounds(400, 46, pEdit.ClientSize.Width - 410, pEdit.ClientSize.Height - 56);
        gR.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        pEdit.Controls.Add(gR);

        var lTitle = new Label(); lTitle.Text = "名称（卡片上显示的标题）："; lTitle.SetBounds(12, 26, 240, 22);
        var tTitle = new TextBox(); tTitle.Font = ui; tTitle.SetBounds(12, 50, gR.ClientSize.Width - 24, 26);
        tTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var lText = new Label(); lText.Text = "内容（点击卡片实际复制的文本）："; lText.SetBounds(12, 86, 280, 22);
        var tText = new TextBox(); tText.Font = mono; tText.Multiline = true;
        tText.ScrollBars = ScrollBars.Vertical;
        tText.SetBounds(12, 110, 400, 200);

        var bSave = new Button(); bSave.Text = "保存修改 (Ctrl+S)";
        bSave.BackColor = Color.FromArgb(228, 242, 255);
        bSave.ForeColor = Color.FromArgb(24, 64, 128);
        bSave.FlatStyle = FlatStyle.Flat;
        bSave.FlatAppearance.BorderColor = Color.FromArgb(165, 198, 238);
        var bCopy = new Button(); bCopy.Text = "复制内容";
        var lblPos = new Label(); lblPos.ForeColor = Color.Gray;
        gR.Controls.Add(lTitle); gR.Controls.Add(tTitle); gR.Controls.Add(lText);
        gR.Controls.Add(tText); gR.Controls.Add(bSave); gR.Controls.Add(bCopy); gR.Controls.Add(lblPos);

        // 统一排版：pEdit 在首次显示前 ClientSize 不可靠，
        // 之前用构造期高度算按钮位置，结果按钮被列表面板挡在底下
        Action layoutEdit = delegate
        {
            int sw = pEdit.ClientSize.Width, sh = pEdit.ClientSize.Height;
            if (sw <= 0 || sh <= 0) return;

            gL.SetBounds(10, 46, 380, Math.Max(140, sh - 56));
            gR.SetBounds(400, 46, Math.Max(240, sw - 410), Math.Max(140, sh - 56));

            int lh = Math.Max(60, gL.ClientSize.Height - 78);
            list.SetBounds(12, 24, Math.Max(80, gL.ClientSize.Width - 24), lh);

            int by = Math.Max(24 + lh + 8, gL.ClientSize.Height - 40);
            bAdd.SetBounds(12, by, 84, 30);
            bDel.SetBounds(102, by, 84, 30);
            bUp.SetBounds(192, by, 84, 30);
            bDown.SetBounds(282, by, 84, 30);

            tTitle.Width = Math.Max(80, gR.ClientSize.Width - 24);
            int th = Math.Max(60, gR.ClientSize.Height - 166);
            tText.SetBounds(12, 110, Math.Max(80, gR.ClientSize.Width - 24), th);

            int ry = Math.Max(110 + th + 8, gR.ClientSize.Height - 44);
            bSave.SetBounds(12, ry, 146, 32);
            bCopy.SetBounds(168, ry, 110, 32);
            lblPos.SetBounds(290, ry + 7, 160, 22);
        };
        pEdit.Resize += delegate { layoutEdit(); };
        gL.Resize += delegate { layoutEdit(); };
        gR.Resize += delegate { layoutEdit(); };
        pEdit.HandleCreated += delegate { try { BeginInvoke((MethodInvoker)delegate { layoutEdit(); }); } catch { } };

        int currentEditIdx = -1;
        bool suppressLoad = false;

        Action commitPendingEdit = delegate
        {
            if (currentEditIdx >= 0 && currentEditIdx < cTitles.Length)
            {
                string newTitle = tTitle.Text.Trim();
                string newText = tText.Text;
                if (newTitle.Length > 0 && (cTitles[currentEditIdx] != newTitle || cTexts[currentEditIdx] != newText))
                {
                    cTitles[currentEditIdx] = newTitle;
                    cTexts[currentEditIdx] = newText;
                    SaveCustom();
                }
            }
        };

        Action<int> loadInto = delegate(int i)
        {
            suppressLoad = true;
            currentEditIdx = i;
            if (i < 0 || i >= cTitles.Length)
            {
                tTitle.Text = ""; tText.Text = ""; lblPos.Text = "";
                tTitle.Enabled = false; tText.Enabled = false; bSave.Enabled = false;
                suppressLoad = false;
                return;
            }
            tTitle.Enabled = true; tText.Enabled = true; bSave.Enabled = true;
            tTitle.Text = cTitles[i];
            tText.Text = cTexts[i];
            lblPos.Text = "第 " + (i + 1) + " / " + cTitles.Length + " 条";
            suppressLoad = false;
        };

        Action rebuildList = delegate
        {
            suppressLoad = true;
            list.BeginUpdate();
            list.Items.Clear();
            for (int i = 0; i < cTitles.Length; i++) list.Items.Add(cTitles[i]);
            list.EndUpdate();
            suppressLoad = false;
            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
                loadInto(0);
            }
            else loadInto(-1);
        };

        Action rebuildCards = null;
        rebuildCards = delegate
        {
            ClearAndDisposeControls(flow);
            string q = txtCardFilter.Text.Trim();
            int shownCards = 0;
            for (int i = 0; i < cTitles.Length; i++)
            {
                if (q.Length > 0)
                {
                    bool match = cTitles[i].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                              || cTexts[i].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!match) continue;
                }
                shownCards++;
                int idx = i;
                var b = new Button();
                b.Tag = "rx-card";
                b.Text = cTitles[i];
                b.Font = cardFont;
                b.Size = new Size(268, 68);
                b.Margin = new Padding(4, 4, 8, 8);
                b.TextAlign = ContentAlignment.MiddleCenter;
                b.BackColor = Color.FromArgb(238, 244, 252);
                b.ForeColor = Color.FromArgb(24, 34, 48);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Color.FromArgb(160, 190, 225);
                b.Cursor = Cursors.Hand;
                b.MouseEnter += delegate { b.BackColor = IsDark ? Color.FromArgb(36, 45, 58) : Color.FromArgb(224, 236, 252); };
                b.MouseLeave += delegate { b.BackColor = IsDark ? Color.FromArgb(28, 35, 46) : Color.FromArgb(238, 244, 252); };
                string tipText = cTexts[idx];
                if (tipText.Length > 600) tipText = tipText.Substring(0, 600) + " …";
                hover.SetToolTip(b, string.IsNullOrEmpty(tipText) ? "（空）左键复制 / 右键管理" : tipText + Environment.NewLine + "（左键单击复制，右键编辑或删除）");
                b.Click += delegate
                {
                    preview.Text = cTexts[idx];
                    preview.Select(0, 0);
                    CopyToClipboard(cTitles[idx], cTexts[idx]);
                };
                b.MouseUp += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button != MouseButtons.Right) return;
                    var menu = new ContextMenuStrip();
                    menu.Closed += delegate { SafeBeginInvoke(delegate { menu.Dispose(); }); };
                    var mCopy = new ToolStripMenuItem("复制内容：" + Shorten(cTitles[idx]));
                    mCopy.Click += delegate { CopyToClipboard(cTitles[idx], cTexts[idx]); };
                    var mEdit = new ToolStripMenuItem("编辑此条目");
                    mEdit.Click += delegate
                    {
                        pCards.Visible = false;
                        pEdit.Visible = true;
                        layoutEdit();
                        rebuildList();
                        if (idx >= 0 && idx < list.Items.Count) list.SelectedIndex = idx;
                        tText.Focus();
                    };
                    var mDel = new ToolStripMenuItem("删除此条目");
                    mDel.Click += delegate
                    {
                        var nt = new string[cTitles.Length - 1];
                        var nx = new string[cTexts.Length - 1];
                        for (int k = 0, j = 0; k < cTitles.Length; k++)
                        {
                            if (k == idx) continue;
                            nt[j] = cTitles[k]; nx[j] = cTexts[k]; j++;
                        }
                        cTitles = nt; cTexts = nx;
                        SaveCustom();
                        rebuildCards();
                        toast.ShowText("已删除条目");
                    };
                    menu.Items.Add(mCopy);
                    menu.Items.Add(mEdit);
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add(mDel);
                    menu.Show(b, e.Location);
                };
                flow.Controls.Add(b);
            }

            if (cTitles.Length == 0)
            {
                var empty = new Label();
                empty.Text = "暂无自定义条目，点击左上角“⚙ 编辑条目”添加常用广播或口令";
                empty.ForeColor = Color.Gray;
                empty.AutoSize = true;
                empty.Margin = new Padding(8, 20, 0, 0);
                flow.Controls.Add(empty);
            }
            else if (shownCards == 0)
            {
                var empty = new Label();
                empty.Text = "没有匹配“" + q + "”的卡片";
                empty.ForeColor = Color.Gray;
                empty.AutoSize = true;
                empty.Margin = new Padding(8, 20, 0, 0);
                flow.Controls.Add(empty);
            }
            ThemeControlTree(flow);
        };

        txtCardFilter.TextChanged += delegate { rebuildCards(); };
        txtCardFilter.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                txtCardFilter.Text = "";
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        list.SelectedIndexChanged += delegate
        {
            if (suppressLoad) return;
            if (currentEditIdx >= 0 && currentEditIdx < cTitles.Length && currentEditIdx != list.SelectedIndex)
            {
                string editedTitle = tTitle.Text.Trim();
                if (editedTitle.Length > 0 && (cTitles[currentEditIdx] != editedTitle || cTexts[currentEditIdx] != tText.Text))
                {
                    cTitles[currentEditIdx] = editedTitle;
                    cTexts[currentEditIdx] = tText.Text;
                    SaveCustom();
                    suppressLoad = true;
                    list.Items[currentEditIdx] = editedTitle;
                    suppressLoad = false;
                }
            }
            loadInto(list.SelectedIndex);
        };

        bAdd.Click += delegate
        {
            commitPendingEdit();
            var nt = new string[cTitles.Length + 1];
            var nx = new string[cTexts.Length + 1];
            Array.Copy(cTitles, nt, cTitles.Length);
            Array.Copy(cTexts, nx, cTexts.Length);
            nt[nt.Length - 1] = "新条目 " + (cTitles.Length + 1);
            nx[nx.Length - 1] = "";
            cTitles = nt; cTexts = nx;
            SaveCustom();
            rebuildList();
            list.SelectedIndex = list.Items.Count - 1;
            tTitle.Focus();
            tTitle.SelectAll();
        };

        bDel.Click += delegate
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= cTitles.Length) return;
            currentEditIdx = -1;
            var nt = new string[cTitles.Length - 1];
            var nx = new string[cTexts.Length - 1];
            for (int k = 0, j = 0; k < cTitles.Length; k++)
            {
                if (k == i) continue;
                nt[j] = cTitles[k]; nx[j] = cTexts[k]; j++;
            }
            cTitles = nt; cTexts = nx;
            SaveCustom();
            rebuildList();
            if (list.Items.Count > 0)
                list.SelectedIndex = Math.Min(i, list.Items.Count - 1);
        };

        Action<int> move = delegate(int dir)
        {
            commitPendingEdit();
            int i = list.SelectedIndex;
            int j = i + dir;
            if (i < 0 || j < 0 || j >= cTitles.Length) return;
            string t = cTitles[i]; cTitles[i] = cTitles[j]; cTitles[j] = t;
            t = cTexts[i]; cTexts[i] = cTexts[j]; cTexts[j] = t;
            SaveCustom();
            currentEditIdx = -1;
            rebuildList();
            list.SelectedIndex = j;
        };
        bUp.Click += delegate { move(-1); };
        bDown.Click += delegate { move(1); };

        Action doSaveCurrent = delegate
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= cTitles.Length) return;
            string name = tTitle.Text.Trim();
            if (name.Length == 0) { toast.ShowText("名称不能为空"); return; }
            cTitles[i] = name;
            cTexts[i] = tText.Text;
            bool ok = SaveCustom();
            int keep = i;
            currentEditIdx = -1;
            rebuildList();
            list.SelectedIndex = keep;
            toast.ShowText(ok ? ("已保存：" + name) : "保存失败，请检查文件权限");
        };

        bSave.Click += delegate { doSaveCurrent(); };

        KeyEventHandler ctrlSHandler = delegate(object s, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                doSaveCurrent();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        tTitle.KeyDown += ctrlSHandler;
        tText.KeyDown += ctrlSHandler;

        bCopy.Click += delegate
        {
            CopyToClipboard(tTitle.Text, tText.Text);
        };

        bEdit.Click += delegate
        {
            pCards.Visible = false;
            pEdit.Visible = true;
            layoutEdit();
            rebuildList();
        };

        bBack.Click += delegate
        {
            commitPendingEdit();
            pEdit.Visible = false;
            pCards.Visible = true;
            rebuildCards();
        };

        if (cTitles.Length == 0)
        {
            cTitles = new string[] { "示例快捷文本" };
            cTexts = new string[] { "点击左上角「⚙ 编辑条目」即可修改或新增你常用的快捷发言、公告或备忘（单击卡片自动复制到剪贴板）" };
            SaveCustom();
        }
        rebuildCards();

        return page;
    }
}
