using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using ApkShellext2.Interop;
using ApkShellext2.Properties;
using System.Globalization;
using System.Configuration;
using System.Diagnostics;

namespace ApkShellext2 {

    public partial class Preferences : Form {
        public Preferences() {
            InitializeComponent();
            // the variable panels are product data, not layout, so they are built here
            // instead of in the designer; Preferences_Load rebuilds them once the UI
            // language is known
            ApplyPatternVariables();
        }

        public string currentFile = "";
        private bool formLoaded = false;
        private bool needClearThumbnailCache = false;

        // the two pattern-variable panels, rebuilt by ApplyPatternVariables
        private TableLayoutPanel pnlRenamePatternVariables;
        private TableLayoutPanel pnlInfoTipPatternVariables;

        private void Preferences_Load(object sender, EventArgs e) {
            Utility.Localize();
            if (AppsUseDarkTheme()) ApplyDarkTheme(this);

            Log("Using setting file from: " + ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath);

            #region Initialize text
            this.Text = Resources.strPreferencesCaption;
            this.Icon = Icon.FromHandle(Utility.ResizeBitmap(Properties.NonLocalizeResources.logo, 16).GetHicon());
            
            btnOK.Text = Resources.btnOK;

            // Tree view on the left
            twLeft.BeginUpdate();
            twLeft.Nodes.Clear();
            twLeft.Nodes.Add(new TreeNode(Resources.twGeneral));
            twLeft.Nodes.Add(new TreeNode(Resources.twIcon));
            twLeft.Nodes.Add(new TreeNode(Resources.twContextMenu));
            twLeft.Nodes.Add(new TreeNode(Resources.twRename));
            twLeft.Nodes.Add(new TreeNode(Resources.twInfotip));
            //twLeft.SelectedNode = twLeft.Nodes[Int16.Parse(Utility.GetSetting("LastPanel","0"))];
            twLeft.ExpandAll();
            twLeft.EndUpdate();

            #region General Panel
            // Dropdown
            if (!formLoaded) {
                CultureInfo[] culs = Utility.getSupportedLanguages();
                foreach (var l in culs) {
                    combLanguage.Items.Add(l.NativeName);
                    if (l.LCID == Thread.CurrentThread.CurrentUICulture.LCID) {
                        combLanguage.Text = l.NativeName;
                    }
                }
                if (combLanguage.Text == "")
                    combLanguage.Text = culs[0].NativeName;
            }

            lblLanguage.Text = Resources.strLanguages;
            lblCurrentVersion.Text = string.Format(Resources.strCurrVersion, Assembly.GetExecutingAssembly().GetName().Version.ToString());
            if (Utility.NewVersionAvailible()) {
                lblNewVer.Text = string.Format(Resources.strNewVersionAvailible, Utility.GetSetting("LatestVersion"));
                btnUpdate.Text = Resources.btnUpdate;
                btnUpdate.Image = Utility.ResizeBitmap(Properties.NonLocalizeResources.iconUpdate, 16);
                toolTip1.SetToolTip(btnUpdate, Resources.btnUpdateToolTip);
            } else {
                lblNewVer.Text = Resources.strGotLatest;
                btnUpdate.Text = Resources.btnGitHub;
                btnUpdate.Image = Utility.ResizeBitmap(Properties.NonLocalizeResources.iconGitHub, 16);
            }

            //btnUpdate.Visible = false;
            toolTip1.SetToolTip(btnUpdate, Resources.strGotoProjectSite);

            chkDebugLog.Text = Resources.strDebugLog;
            chkDebugLog.Checked = Utility.GetSetting("DebugLog", "False") == "True";

            #endregion

            #region Icon Panel
            ckShowOverlay.Text = Resources.strShowOverlayIcon;
            toolTip1.SetToolTip(ckShowOverlay, Resources.strShowOverlayIconToolTip);
            ckShowOverlay.Checked = Utility.GetSetting("ShowOverLayIcon","False")=="True";
            ckShowIPA.Text = Resources.strShowIpaIcon;
            ckShowIPA.Checked = Utility.GetSetting("ShowIpaIcon","True") == "True";
            ckShowAppxIcon.Text = Resources.strShowAppxIcon;
            ckShowAppxIcon.Checked = Utility.GetSetting("ShowAppxIcon","False") == "True";
            ckStretchThumbnail.Text = Resources.strStretchThumbnail;
            ckStretchThumbnail.Checked = Utility.GetSetting("StretchThumbnail","True") == "True";
            ckEnableThumbnail.Text = Resources.strEnableThumbnail;
            ckEnableThumbnail.Checked = Utility.GetSettingBool("EnableThumbnail", true);
            ckAdaptiveIconSupport.Text = Resources.strSupportAdaptiveIcon;
            ckAdaptiveIconSupport.Checked = Utility.GetSetting("SupportAdaptiveIcon", "False") == "True";

            btnClearCache.Text = Resources.strClearCache;
            #endregion

            #region ContextMenu Panel
            ckAlwaysShowStore.Text = Resources.strAlwaysShowGooglePlay;
            toolTip1.SetToolTip(ckAlwaysShowStore, Resources.strAlwaysShowGooglePlayToolTip);
            ckAlwaysShowStore.Checked = Utility.GetSetting("ShowAppStoreWhenMultiSelected") == "True";
            ckShowMenuIcon.Checked = Utility.GetSetting("ShowMenuIcon") == "True";
            ckShowMenuIcon.Text = Resources.strShowContextMenuIcon;
            ckShowNewVersionInfo.Checked = Utility.GetSetting("ShowNewVersion") == "True";
            ckShowNewVersionInfo.Text = Resources.strShowNewVerInfo;
            ckShowGoogle.Checked = Utility.GetSetting("ShowGooglePlay") == "True";
            ckShowGoogle.Text = Resources.strShowGooglePlay;
            ckShowAM.Checked = Utility.GetSetting("ShowApkMirror") == "True";
            ckShowAM.Text = Resources.strShowApkMirror;
            ckShowAmazon.Checked = Utility.GetSetting("ShowAmazonStore") == "True";
            ckShowAmazon.Text = Resources.strShowAmazonStore;
            ckShowApple.Checked = Utility.GetSetting("ShowAppleStore") == "True";
            ckShowApple.Text = Resources.strShowAppleStore;
            ckShowMS.Checked = Utility.GetSetting("ShowMSStore") == "True";
            ckShowMS.Text = Resources.strShowMSStore;
            ckShowAM.Checked = Utility.GetSetting("ShowApkMirror") == "True";
            ckShowAM.Text = Resources.strShowApkMirror;
            #endregion

            #region Renaming Panel
            lblRenamePattern.Text = Resources.strRenamePattern;
            txtRenamePattern.Text = Utility.GetSetting("RenamePattern", NonLocalizeResources.strRenamePatternDefault);

            btnResetRenamePattern.Text = Resources.btnResetPattern;
            btnResetRenamePattern_Click(this, new EventArgs());

            ckReplaceSpace.Text = Resources.strReplaceSpaceWith_;
            ckReplaceSpace.Checked = Utility.GetSetting("ReplaceSpace", "False") == "True";
            txtReplaceWhiteSpace.Enabled = ckReplaceSpace.Checked;
            txtReplaceWhiteSpace.Text = Utility.GetSetting("ReplaceSpaceChar", "_");

            ckUseAppNameLang.Text = Resources.strUseAppNameLang;
            ckUseAppNameLang.Checked = Utility.GetSetting("UseAppNameLanguage", "False") == "True";
            if (!appLangCulturesLoaded) {
                combAppNameLang.Items.Add(Resources.strFollowUiLanguage);
                appLangCultures.Add(null);
                foreach (CultureInfo cul in Utility.getSupportedLanguages()) {
                    combAppNameLang.Items.Add(cul.NativeName);
                    appLangCultures.Add(cul);
                }
                appLangCulturesLoaded = true;
            }
            string appNameLang = Utility.GetSetting("AppNameLanguage", "");
            int appNameLangIndex = 0;
            for (int i = 1; i < appLangCultures.Count; i++) {
                if (string.Equals(appLangCultures[i].Name, appNameLang, StringComparison.OrdinalIgnoreCase)) {
                    appNameLangIndex = i;
                    break;
                }
            }
            combAppNameLang.SelectedIndex = appNameLangIndex;
            combAppNameLang.Enabled = ckUseAppNameLang.Checked;
            #endregion

            #region ToolTip Panel
            lblInfoTipPattern.Text = Resources.strInfoTipPattern;
            txtToolTipPattern.Text = Utility.GetSetting("ToolTipPattern", NonLocalizeResources.strInfoTipDefault);
            
            btnResetInfoTipPattern.Text = Resources.btnResetPattern;

            // the token descriptions and the list are localized, so rebuild both panels
            // with the UI language that was just selected
            ApplyPatternVariables();
            #endregion

            #endregion

            twLeft.Select();
            pnlRight.Visible = true;
            btnOK.Focus();
            formLoaded = true;
        }

        private void DisablePenels() {
            pnlContextMenu.Visible = false;
            pnlGeneral.Visible = false;
            pnlIcon.Visible = false;
            pnlRenaming.Visible = false;
            pnlInfoTip.Visible = false;
            btnResetInfoTipPattern.Visible = false;
            btnResetRenamePattern.Visible = false;
            btnUpdate.Visible = false;
        }

        private void combLanguage_SelectedIndexChanged(object sender, EventArgs e) {
            CultureInfo[] culs = Utility.getSupportedLanguages();
            CultureInfo cul = culs[combLanguage.SelectedIndex];
            Utility.SaveSetting("Language", cul.Name);
            if (formLoaded)
                this.OnLoad(e);
        }

        private void ckShowIPA_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("ShowIpaIcon", ckShowIPA.Checked);
            Utility.refreshShell();
        }

        private void chkDebugLog_CheckedChanged(object sender, EventArgs e) {
            // Read by the shell hosts at extension load time; takes effect after they reload.
            Utility.SaveSetting("DebugLog", chkDebugLog.Checked);
        }

        private void ckShowAppxIcon_CheckedChanged(object sender, EventArgs e) {

            Utility.SaveSetting("ShowAppxIcon",ckShowAppxIcon.Checked);       
            NativeMethods.NotifyAssociationsChanged();
        }

        private void ckShowOverlay_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("ShowOverLayIcon", ckShowOverlay.Checked);
            if (formLoaded) {
                NativeMethods.NotifyAssociationsChanged();
                if (Utility.GetSettingBool("EnableThumbnail"))
                    needClearThumbnailCache = true;
            }
        }

        private void btnUpdate_Click(object sender, EventArgs e) {
            System.Diagnostics.Process.Start(string.Format(NonLocalizeResources.urlGithubHomeWithVersion, Assembly.GetExecutingAssembly().GetName().Version.ToString()));
        }

        private void ckShowMenuIcon_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("ShowMenuIcon", ckShowMenuIcon.Checked);
        }

        private void ckShowPlay_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("ShowAppStoreWhenMultiSelected", ckAlwaysShowStore.Checked);
        }

        private void twLeft_AfterSelect(object sender, TreeViewEventArgs e) {
            DisablePenels();
            if (twLeft.SelectedNode.Text == Resources.twGeneral) {
                pnlGeneral.Visible = true;
                btnUpdate.Visible = true;
            } else if (twLeft.SelectedNode.Text == Resources.twIcon) {
                pnlIcon.Visible = true;
            } else if (twLeft.SelectedNode.Text == Resources.twRename) {
                pnlRenaming.Visible = true;
                btnResetRenamePattern.Visible = true;
            } else if (twLeft.SelectedNode.Text == Resources.twContextMenu) {
                pnlContextMenu.Visible = true;
            } else if (twLeft.SelectedNode.Text == Resources.twInfotip) {
                pnlInfoTip.Visible = true;
                btnResetInfoTipPattern.Visible = true;
            }
            Utility.SaveSetting("LastPanel", twLeft.SelectedNode.Index);
        }

        private void btnOK_Click(object sender, EventArgs e) {
            if (RenamePatternIsDirty)
                Utility.SaveSetting("RenamePattern", txtRenamePattern.Text);
            if (ToolTipPatternIsDirty)
                Utility.SaveSetting("ToolTipPattern", txtToolTipPattern.Text);
            if (txtReplaceWhiteSpaceDirty)
                Utility.SaveSetting("ReplaceSpaceChar", txtReplaceWhiteSpace.Text);
            this.Close();
        }

        private void ckShowAmazon_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("ShowAmazonStore", ckShowAmazon.Checked);
        }

        private void ckbShowGoogle_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("ShowGooglePlay", ckShowGoogle.Checked);
        }

        private void btnResetTooltipPattern_Click(object sender, EventArgs e) {
            txtToolTipPattern.Text = NonLocalizeResources.strInfoTipDefault;
        }

        private void btnResetRenamePattern_Click(object sender, EventArgs e) {
            txtRenamePattern.Text = Utility.GetSetting("RenamePattern", NonLocalizeResources.strRenamePatternDefault);
        }

        private void ckShowApple_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("ShowAppleStore",ckShowApple.Checked);
        }

        private void ckShowMS_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("ShowMSStore", ckShowMS.Checked);
        }

        private void ckShowAM_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("ShowApkMirror", ckShowAM.Checked);
        }

        private bool RenamePatternIsDirty = false;
        private void txtRename_TextChanged(object sender, EventArgs e) {
                RenamePatternIsDirty = formLoaded;
        }

        private bool ToolTipPatternIsDirty = false;
        private void txtToolTipPattern_TextChanged(object sender, EventArgs e) {
                ToolTipPatternIsDirty = formLoaded;
        }

        private void Preferences_FormClosed(object sender, FormClosedEventArgs e) {
            if (needClearThumbnailCache) {
                if (System.Windows.Forms.MessageBox.Show(
                    Resources.dialogNeedClearCache,
                    Resources.strClearCache,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Exclamation) == DialogResult.Yes) {
                        btnClearCache_Click(sender, new EventArgs());
                }
            }
        }

        private void ckReplaceSpace_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("ReplaceSpace", ckReplaceSpace.Checked);
            txtReplaceWhiteSpace.Enabled = ckReplaceSpace.Checked;
        }

        private void btnUpdate_Click_1(object sender, EventArgs e) {
            System.Diagnostics.Process.Start(string.Format(Properties.NonLocalizeResources.urlGithubHomeWithVersion,Assembly.GetExecutingAssembly().GetName().Version.ToString()));
        }

        private void ckStretchIcon_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("StretchThumbnail", ckStretchThumbnail.Checked);
            needClearThumbnailCache = formLoaded;
        }

        private void ckEnableThumbnail_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("EnableThumbnail",ckEnableThumbnail.Checked);
            needClearThumbnailCache = formLoaded;
        }

        private void Log(string message) {
            Utility.Log(this, "", message);
        }

        private void btnClearCache_Click(object sender, EventArgs e) {
            if (System.Windows.Forms.MessageBox.Show(
                    Resources.dialogClearCache,
                    Resources.strClearCache,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Exclamation) == DialogResult.Yes) {
                Process process = new Process();
                // random file name: a fixed %TEMP%\clearcache.bat lets another
                // same-user process rewrite it between our write and Start()
                string path = Path.Combine(Path.GetTempPath(), "clearcache" + Path.GetRandomFileName() + ".bat");
                File.WriteAllText(path, NonLocalizeResources.cmdClearCache);
                process.StartInfo.FileName = path;
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                process.Start();
            }
        }

        private void CkShowNewVersionInfo_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("ShowNewVersion", ckShowNewVersionInfo.Checked);
        }

        private void CkAdaptiveIconSupport_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("SupportAdaptiveIcon", ckAdaptiveIconSupport.Checked);
            needClearThumbnailCache = formLoaded;
        }

        private readonly List<CultureInfo> appLangCultures = new List<CultureInfo>();
        private bool appLangCulturesLoaded = false;

        private void CkUseAppNameLang_CheckedChanged(object sender, EventArgs e) {
            Utility.SaveSetting("UseAppNameLanguage", ckUseAppNameLang.Checked);
            combAppNameLang.Enabled = ckUseAppNameLang.Checked;
        }

        private void CombAppNameLang_SelectedIndexChanged(object sender, EventArgs e) {
            int i = combAppNameLang.SelectedIndex;
            if (i < 0 || i >= appLangCultures.Count) return;
            CultureInfo cul = appLangCultures[i];
            // index 0 == 'follow UI language': empty setting makes requestedNameCulture() use CurrentUICulture
            Utility.SaveSetting("AppNameLanguage", cul == null ? "" : cul.Name);
        }

        private void Label1_Click(object sender, EventArgs e) {

        }

        private bool txtReplaceWhiteSpaceDirty = false;
        private void TxtReplaceWhiteSpace_TextChanged(object sender, EventArgs e) {
            txtReplaceWhiteSpaceDirty = formLoaded;
        }

        private void Preferences_ResizeBegin(object sender, EventArgs e) {
            
        }

        #region Pattern variable insertion
        // Both pattern pages list exactly the tokens CommandRename.ReplaceVariables
        // expands, taken from RenameVariableCatalog, so the UI cannot drift from the
        // command. The wikilink is kept as the MoreInfo entry, but the variables are now
        // offered in place and can be copied whole.
        private void ApplyPatternVariables() {
            pnlRenamePatternVariables = ReplacePatternVariablePanel(pnlRenaming, 4,
                pnlRenamePatternVariables, "pnlRenamePatternVariables", txtRenamePattern,
                PatternVariablePages.Rename);

            pnlInfoTipPatternVariables = ReplacePatternVariablePanel(pnlInfoTip, 7,
                pnlInfoTipPatternVariables, "pnlInfoTipPatternVariables", txtToolTipPattern,
                PatternVariablePages.InfoTip);
        }

        private TableLayoutPanel ReplacePatternVariablePanel(TableLayoutPanel host, int row,
                TableLayoutPanel old, string name, TextBox patternBox, PatternVariablePages page) {
            if (old != null) {
                host.Controls.Remove(old);
                old.Dispose();
            }
            TableLayoutPanel panel = CreatePatternVariablePanel(name, patternBox, page);
            while (host.RowStyles.Count <= row) host.RowStyles.Add(new RowStyle());
            // a fixed row height would clip the tokens once the language changes
            host.RowStyles[row] = new RowStyle(SizeType.AutoSize);
            host.Controls.Add(panel, 0, row);
            if (AppsUseDarkTheme()) ApplyDarkTheme(panel);
            return panel;
        }

        private TableLayoutPanel CreatePatternVariablePanel(string name, TextBox patternBox,
                PatternVariablePages page) {
            TableLayoutPanel panel = new TableLayoutPanel();
            panel.Name = name;
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.ColumnCount = 1;
            panel.ColumnStyles.Add(new ColumnStyle());
            panel.Dock = DockStyle.Fill;
            panel.RowCount = 3;
            panel.RowStyles.Add(new RowStyle());
            panel.RowStyles.Add(new RowStyle());
            panel.RowStyles.Add(new RowStyle());
            panel.Controls.Add(CreatePatternVariableLinks(name + "Links", patternBox, page), 0, 0);

            TextBox list = new TextBox();
            list.Name = name + "List";
            list.Dock = DockStyle.Fill;
            list.Multiline = true;
            list.ReadOnly = true;
            list.ScrollBars = ScrollBars.Vertical;
            list.WordWrap = false;
            list.TabStop = true;
            // a docked multiline textbox always asks for one line: the minimum size is
            // what keeps the whole catalog visible
            list.MinimumSize = new Size(0, 96);
            list.Text = RenameVariableCatalog.Describe(page);
            toolTip1.SetToolTip(list, Resources.strPatternVariableListHint);
            panel.Controls.Add(list, 0, 1);

            LinkLabel moreInfo = new LinkLabel();
            moreInfo.Name = name + "MoreInfo";
            moreInfo.Text = Resources.strPatternVariablesMoreInfo;
            moreInfo.AutoSize = true;
            moreInfo.Margin = new Padding(0, 4, 0, 0);
            moreInfo.TabStop = true;
            toolTip1.SetToolTip(moreInfo, Resources.strReferToWiki);
            moreInfo.LinkClicked += delegate {
                Process.Start(NonLocalizeResources.urlPatternVarWiki);
            };
            panel.Controls.Add(moreInfo, 0, 2);
            return panel;
        }

        private FlowLayoutPanel CreatePatternVariableLinks(string name, TextBox patternBox,
                PatternVariablePages page) {
            FlowLayoutPanel strip = new FlowLayoutPanel();
            strip.Name = name;
            strip.AutoSize = true;
            strip.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            strip.Dock = DockStyle.Fill;
            strip.WrapContents = true;
            toolTip1.SetToolTip(strip, Resources.strPatternVariableInsertHint);
            // the column is auto-sized from its widest child, so the strip has to be
            // bounded by the pattern box width or it would widen the whole page
            strip.MaximumSize = new Size(patternBox.Width, 0);
            foreach (PatternVariable variable in RenameVariableCatalog.For(page)) {
                LinkLabel link = new LinkLabel();
                link.Text = variable.Token;
                link.AutoSize = true;
                link.Margin = new Padding(0, 0, 12, 2);
                link.TabStop = true;
                toolTip1.SetToolTip(link, variable.Description);
                string token = variable.Token;
                link.LinkClicked += delegate { InsertPatternVariable(patternBox, token); };
                strip.Controls.Add(link);
            }
            return strip;
        }

        /// <summary>
        /// Applies a token click: the catalog's PatternInsertion replaces the selection
        /// when there is one, otherwise it inserts at the caret, and the caret is placed
        /// right after the inserted token so typing can continue there.
        /// </summary>
        private static void InsertPatternVariable(TextBox patternBox, string token) {
            PatternInsertion insertion = RenameVariableCatalog.Insert(patternBox.Text,
                patternBox.SelectionStart, patternBox.SelectionLength, token);
            patternBox.Text = insertion.Text;
            patternBox.SelectionStart = insertion.CaretPosition;
            patternBox.SelectionLength = 0;
            patternBox.Focus();
        }
        #endregion

        #region Windows 10/11 dark mode
        // Follows the per-user "Choose your app mode" (Settings → Personalization → Colors).
        // HKCU\...\Personalize\AppsUseLightTheme = 0 means the shell runs apps in dark mode;
        // a light WinForms dialog inside dark Explorer is the classic "forgot the theme" look.
        private static bool AppsUseDarkTheme() {
            try {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            } catch { return false; }
        }

        private static readonly Color DarkBack = Color.FromArgb(32, 32, 32);
        private static readonly Color DarkField = Color.FromArgb(43, 43, 43);
        private static readonly Color DarkFore = Color.FromArgb(238, 238, 238);
        private static readonly Color DarkBorder = Color.FromArgb(70, 70, 70);

        private static void ApplyDarkTheme(Control c) {
            var tb = c as TextBox;
            var cb = c as ComboBox;
            var tv = c as TreeView;
            var btn = c as Button;
            if (tb != null) {
                tb.BackColor = DarkField;
                tb.ForeColor = DarkFore;
                tb.BorderStyle = BorderStyle.FixedSingle;
            } else if (cb != null) {
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = DarkField;
                cb.ForeColor = DarkFore;
            } else if (tv != null) {
                tv.BackColor = DarkBack;
                tv.ForeColor = DarkFore;
                tv.BorderStyle = BorderStyle.FixedSingle;
            } else if (btn != null) {
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = DarkField;
                btn.ForeColor = DarkFore;
                btn.FlatAppearance.BorderColor = DarkBorder;
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            } else if (c is Label || c is CheckBox || c is LinkLabel) {
                c.ForeColor = DarkFore;
            } else {
                c.BackColor = DarkBack;
                c.ForeColor = DarkFore;
            }
            foreach (Control child in c.Controls)
                ApplyDarkTheme(child);
        }

        private void EnableDarkTitleBar() {
            // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on 2004+/Win11, 19 on 1809-1903.
            try {
                int on = 1;
                if (DwmSetWindowAttribute(this.Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(this.Handle, 19, ref on, sizeof(int));
            } catch { /* dwmapi missing on ancient builds - stay light */ }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            if (AppsUseDarkTheme()) EnableDarkTitleBar();
        }
        #endregion
    }
}
