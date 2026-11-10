using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.IO.Compression;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DnfLauncher
{
    static class Cfg
    {
        public static string ExeDir;
        public static string IniPath;
        public static string IP = "";            // 房主的联机 IP（Radmin / ZeroTier 等），首次运行在界面里填
        public static string HostIp = "";          // 朋友端：房主的 Radmin IP
        public static string VmIp = "192.168.200.131";
        public static string ToolsDir;
        public static string ClientDir;
        public static string Launcher = "DOFLogin.exe";   // 客户端目录里的游戏启动器文件名（可换）
        public static string Python = "python.exe";
        public static bool Robot = true;          // 一键启动时是否先拉起假人系统（/root/robot）
        public static string RobotPkg = @"";   // 假人部署包目录（里面有 robot、config\）
        public static bool RobotReady = false;    // 是否已经问过/做过假人部署
        public static int SshPort = 22;
        public static string SshUser = "root";
        public static string SshPwd = "";        // 服务端 SSH 密码，首次部署时填写，不要写进仓库
        public static string RemoteGameDir = "/home/neople/game";   // 服务端里放密钥的目录
        public static string RobotWebPwd = "";   // 假人管理页密码（自动登录用）
        public static string VmRun = @"D:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe";
        public static string Vmx = @"E:\DNF\70S3A3\CentOS 7.6\CentOS 7.6.vmx";

        public static void Init()
        {
            ExeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            IniPath = Path.Combine(ExeDir, "启动器配置.ini");
            ToolsDir = ExeDir;
            ClientDir = Path.GetFullPath(Path.Combine(ExeDir, "..\\客户端"));
            Load();
            // 优先用工具自带的绿色版 Python（python\python.exe），这样换电脑不用装 Python
            string bundled = Path.Combine(ExeDir, "python\\python.exe");
            if (File.Exists(bundled)) Python = bundled;
            // 假人部署包：优先用工具目录里自带的那份（假人部署包\），换电脑也能直接部署
            string localPkg = Path.Combine(ExeDir, "假人部署包");
            if (File.Exists(Path.Combine(localPkg, "robot"))) RobotPkg = localPkg;
            if (!File.Exists(IniPath)) Save();
        }

        public static void Load()
        {
            if (!File.Exists(IniPath)) return;
            foreach (string raw in File.ReadAllLines(IniPath, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || !line.Contains("=")) continue;
                string k = line.Substring(0, line.IndexOf('=')).Trim().ToUpperInvariant();
                string v = line.Substring(line.IndexOf('=') + 1).Trim();
                switch (k)
                {
                    case "IP": IP = v; break;
                    case "HOSTIP": HostIp = v; break;
                    case "VMIP": VmIp = v; break;
                    case "TOOLSDIR": ToolsDir = v; break;
                    case "CLIENTDIR": ClientDir = v; break;
                    case "LAUNCHER": Launcher = v; break;
                    case "PYTHON": Python = v; break;
                    case "ROBOT": Robot = (v == "1" || v.ToUpperInvariant() == "TRUE" || v.ToUpperInvariant() == "YES"); break;
                    case "ROBOTPKG": RobotPkg = v; break;
                    case "ROBOTREADY": RobotReady = (v == "1" || v.ToUpperInvariant() == "TRUE" || v.ToUpperInvariant() == "YES"); break;
                    case "SSHPORT": try { SshPort = int.Parse(v); } catch { } break;
                    case "SSHUSER": SshUser = v; break;
                    case "SSHPWD": SshPwd = v; break;
                    case "GAMEDIR": RemoteGameDir = v; break;
                    case "ROBOTWEBPWD": RobotWebPwd = v; break;
                    case "VMRUN": VmRun = v; break;
                    case "VMX": Vmx = v; break;
                }
            }
        }

        public static void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# DNF 联机启动器配置（改完保存即可，重启启动器生效）");
            sb.AppendLine("IP=" + IP);
            sb.AppendLine("HOSTIP=" + HostIp);
            sb.AppendLine("VMIP=" + VmIp);
            sb.AppendLine("TOOLSDIR=" + ToolsDir);
            sb.AppendLine("CLIENTDIR=" + ClientDir);
            sb.AppendLine("LAUNCHER=" + Launcher);
            sb.AppendLine("PYTHON=" + Python);
            sb.AppendLine("ROBOT=" + (Robot ? "1" : "0"));
            sb.AppendLine("ROBOTPKG=" + RobotPkg);
            sb.AppendLine("ROBOTREADY=" + (RobotReady ? "1" : "0"));
            sb.AppendLine("SSHPORT=" + SshPort);
            sb.AppendLine("SSHUSER=" + SshUser);
            sb.AppendLine("SSHPWD=" + SshPwd);
            sb.AppendLine("GAMEDIR=" + RemoteGameDir);
            sb.AppendLine("ROBOTWEBPWD=" + RobotWebPwd);
            sb.AppendLine("VMRUN=" + VmRun);
            sb.AppendLine("VMX=" + Vmx);
            File.WriteAllText(IniPath, sb.ToString(), new UTF8Encoding(false));
        }

        public static string Script(string name)
        {
            string sub = Path.Combine(ToolsDir, "scripts", name);
            return File.Exists(sub) ? sub : Path.Combine(ToolsDir, name);
        }
    }

    public class MainForm : Form
    {
        // 配色
        static readonly Color Bg = Color.FromArgb(24, 26, 34);
        static readonly Color PanelBg = Color.FromArgb(32, 35, 46);
        static readonly Color Accent = Color.FromArgb(64, 158, 255);
        static readonly Color TextMain = Color.FromArgb(232, 236, 244);
        static readonly Color TextDim = Color.FromArgb(140, 148, 166);
        static readonly Color OkColor = Color.FromArgb(82, 196, 130);
        static readonly Color BadColor = Color.FromArgb(235, 100, 100);
        static readonly Color WarnColor = Color.FromArgb(230, 180, 80);

        TextBox _log;
        Label _lblRadmin, _lblProxy, _lblServer, _lblBusy, _lblInfo, _lblIp, _lblPing;
        Button _btnPath, _btnHost;
        CheckBox _chkRobot;
        FlowLayoutPanel _buttons;
        System.Windows.Forms.Timer _timer;
        NotifyIcon _tray;
        EventWaitHandle _showEvent;
        bool _reallyExit;
        volatile bool _busy;
        public readonly bool IsHost;   // true=房主端，false=朋友端

        public MainForm(bool isHost)
        {
            IsHost = isHost;
            Text = "DNF联机工具V1.0";
            ClientSize = new Size(940, 768);
            MinimumSize = new Size(880, 700);
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // 注意：WinForms 的 Dock 按 z 序逆序处理，Fill 控件必须最先加入
            BuildLog();
            BuildFooter();
            BuildButtons();
            BuildStatus();
            BuildHeader();
            BuildTray();

            Shown += delegate
            {
                if (IsHost)
                {
                    ShowTutorial();
                    CheckPaths();
                    BeginInvoke(new Action(EnsureRobotDeployed));
                }
                else
                {
                    ShowFriendTutorial();
                    RefreshButtons();
                    if (string.IsNullOrEmpty(Cfg.HostIp)) BeginInvoke(new Action(AutoDetectHost));
                }
                RefreshStatus();
            };
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 4000;
            _timer.Tick += delegate { RefreshStatus(); };
            _timer.Start();
            FormClosing += OnFormClosing;
            FitHeight();
            Shown += delegate { RefitHeight(); };
        }

        // ---------- 界面 ----------
        void BuildHeader()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Top;
            p.Height = 62;
            p.BackColor = PanelBg;
            Controls.Add(p);

            Label t = new Label();
            t.Text = "DNF联机工具V1.0";
            t.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
            t.ForeColor = TextMain;
            t.AutoSize = true;
            t.Location = new Point(22, 16);
            p.Controls.Add(t);

            _lblIp = new Label();
            _lblIp.Text = "Radmin 未连接";
            _lblIp.Font = new Font("Consolas", 13F, FontStyle.Bold);
            _lblIp.ForeColor = Accent;
            _lblIp.AutoSize = true;
            _lblIp.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblIp.Location = new Point(p.Width - 200, 20);
            p.Controls.Add(_lblIp);
            p.Resize += delegate { _lblIp.Left = p.Width - _lblIp.Width - 26; };
        }

        Panel _statusPanel;
        void BuildStatus()
        {
            _statusPanel = new Panel();
            _statusPanel.Dock = DockStyle.Top;
            _statusPanel.Height = 44;
            _statusPanel.BackColor = Bg;
            _statusPanel.Padding = new Padding(20, 8, 20, 4);
            Controls.Add(_statusPanel);

            _lblRadmin = MakeStatus("● Radmin 网卡", 20);
            if (IsHost)
            {
                _lblProxy = MakeStatus("● 端口转发", 190);
                _lblServer = MakeStatus("● 服务端", 360);
                _lblPing = MakeStatus("延迟 --", 520);
                _lblInfo = new Label();
                _lblInfo.Text = "";
                _lblInfo.AutoSize = true;
                _lblInfo.ForeColor = TextDim;
                _lblInfo.Location = new Point(660, 12);
                _statusPanel.Controls.Add(_lblInfo);
            }
            else
            {
                _lblServer = MakeStatus("● 服务端", 190);
                _lblInfo = new Label();
                _lblInfo.Text = "房主地址：未设置";
                _lblInfo.AutoSize = true;
                _lblInfo.ForeColor = TextDim;
                _lblInfo.Location = new Point(340, 12);
                _statusPanel.Controls.Add(_lblInfo);
                _lblPing = MakeStatus("延迟 --", 620);
            }
        }

        Label MakeStatus(string text, int x)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, 12);
            l.ForeColor = TextDim;
            l.Font = new Font("Microsoft YaHei UI", 9.5F);
            _statusPanel.Controls.Add(l);
            return l;
        }

        void BuildButtons()
        {
            Panel left = new Panel();
            left.Dock = DockStyle.Left;
            left.Width = 284;
            left.Padding = new Padding(16, 8, 10, 8);
            left.BackColor = Bg;
            Controls.Add(left);

            _buttons = new FlowLayoutPanel();
            _buttons.Dock = DockStyle.Fill;
            _buttons.FlowDirection = FlowDirection.TopDown;
            _buttons.WrapContents = false;
            _buttons.AutoScroll = true;
            _buttons.BackColor = Bg;
            left.Controls.Add(_buttons);

            if (IsHost)
            {
                CheckBox chk = new CheckBox();
                chk.Text = "启动假人系统（./robot）";
                chk.AutoSize = false;
                chk.Width = 214;
                chk.Height = 26;
                chk.Checked = Cfg.Robot;
                chk.ForeColor = TextMain;
                chk.BackColor = Bg;
                chk.Font = new Font("Microsoft YaHei UI", 9.5F);
                chk.Margin = new Padding(6, 6, 6, 2);
                chk.Cursor = Cursors.Hand;
                chk.CheckedChanged += delegate { Cfg.Robot = chk.Checked; Cfg.Save(); };
                _chkRobot = chk;
                _buttons.Controls.Add(chk);

                AddGroup("路径设置");
                AddButton("设置客户端路径", false, delegate { SetClientPath(); });
                AddButton("设置服务端路径", false, delegate { SetServerPath(); });
                AddButton("一键启动联机", true, delegate { OneClickStart(); });
                AddButton("启动游戏", false, delegate { StartGame(); });
                AddButton("一键检查状态", false, delegate { RunScript("dnf_check.py", "", "状态自检"); });
                AddGroup("假人系统");
                AddButton("假人管理", false, delegate { OpenRobotManager(); });
                AddButton("假人参数", false, delegate { OpenRobotConfig(); });
                AddButton("重新部署假人", false, delegate { DeployRobot(true); });
                AddGroup("关闭");
                AddButton("停止服务端", false, delegate { RunScript("dnf_server_ctl.py", "stop", "停止服务端"); });
                AddButton("停止端口转发", false, delegate { RunScript("dnf_proxy_ctl.py", "stop", "停止端口转发"); });
                AddGroup("设置");
                AddButton("修改联机 IP", false, delegate { ChangeIp(); });
                AddGroup("帮助");
                AddButton("联机教程", false, delegate { ShowTutorial(); });
                AddButton("环境自检", false, delegate { OpenEnvCheck(); });
                AddButton("查看转发日志", false, delegate { OpenFile(Path.Combine(Cfg.ToolsDir, "proxy.log")); });
            }
            else
            {
                _btnPath = AddButton("游戏路径：未设置", false, delegate { SetClientPath(); });
                _btnHost = AddButton("房主 IP：未设置", false, delegate { SetHostIp(); });
                AddButton("一键启动联机", true, delegate { OneClickStart(); });
                AddButton("启动游戏", false, delegate { StartGame(); });
                AddButton("一键检查状态", false, delegate { CheckFriendStatus(); });
                AddGroup("帮助");
                AddButton("联机教程", false, delegate { ShowFriendTutorial(); });
                AddButton("环境自检", false, delegate { OpenEnvCheck(); });
            }

            // 左下角常驻【退出】（不跟着按钮区滚动）
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 56;
            bottom.BackColor = Bg;
            left.Controls.Add(bottom);

            Button quit = new RoundButton();
            quit.Text = "退出";
            quit.Width = 214;
            quit.Height = 42;
            quit.Location = new Point(6, 8);
            quit.FlatStyle = FlatStyle.Flat;
            quit.FlatAppearance.BorderSize = 1;
            quit.FlatAppearance.BorderColor = Color.FromArgb(96, 52, 58);
            quit.BackColor = PanelBg;
            quit.ForeColor = BadColor;
            quit.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            quit.Cursor = Cursors.Hand;
            quit.Click += delegate
            {
                if (_busy) { Log("正在执行上一个操作，稍等几秒再退出。"); return; }
                if (IsHost) ExitAndStop();
                else ExitApp();
            };
            bottom.Controls.Add(quit);
        }

        void FitHeight()
        {
            int need = 0;
            foreach (Control c in _buttons.Controls) need += c.Height + c.Margin.Vertical;
            int chrome = 62 + 44 + 30 + 14 + 56 + 8;
            int h = need + chrome;
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int maxH = wa.Height - 40;
            if (h > maxH) h = maxH;
            if (h < 620) h = 620;
            ClientSize = new Size(ClientSize.Width, h);

            int w = Width, hh = Height;
            int top = Top, left = Left;
            if (top + hh > wa.Bottom - 4) top = wa.Bottom - hh - 4;
            if (top < wa.Top + 4) top = wa.Top + 4;
            if (left + w > wa.Right - 4) left = wa.Right - w - 4;
            if (left < wa.Left + 4) left = wa.Left + 4;
            Location = new Point(left, top);
        }

        void RefitHeight()
        {
            if (_buttons == null || _buttons.Controls.Count == 0) return;
            int contentBottom = 0;
            foreach (Control c in _buttons.Controls)
                contentBottom = Math.Max(contentBottom, c.Bottom + c.Margin.Bottom);

            int offset = _buttons.PointToScreen(Point.Empty).Y - PointToScreen(Point.Empty).Y;
            int below = ClientSize.Height - (offset + _buttons.Height);
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int h = offset + contentBottom + below + 4;
            if (h < 620) h = 620;
            if (h > wa.Height - 40) h = wa.Height - 40;
            if (h != ClientSize.Height) ClientSize = new Size(ClientSize.Width, h);

            int top = Top, left = Left, w = Width, hh = Height;
            if (top + hh > wa.Bottom - 4) top = wa.Bottom - hh - 4;
            if (top < wa.Top + 4) top = wa.Top + 4;
            if (left + w > wa.Right - 4) left = wa.Right - w - 4;
            if (left < wa.Left + 4) left = wa.Left + 4;
            Location = new Point(left, top);
        }

        void RefreshButtons()
        {
            if (_btnPath != null)
                _btnPath.Text = "游戏路径：" + (string.IsNullOrEmpty(Cfg.ClientDir) ? "未设置" : Shorten(Cfg.ClientDir, 18));
            if (_btnHost != null)
                _btnHost.Text = "房主 IP：" + (string.IsNullOrEmpty(Cfg.HostIp) ? "未设置" : Cfg.HostIp);
        }

        static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            int keep = max - 1;
            int head = keep / 2;
            int tail = keep - head;
            return s.Substring(0, head) + "…" + s.Substring(s.Length - tail);
        }

        void AddGroup(string title)
        {
            Label l = new Label();
            l.Text = title;
            l.AutoSize = false;
            l.Width = 214;
            l.Height = 22;
            l.TextAlign = ContentAlignment.BottomLeft;
            l.ForeColor = TextDim;
            l.Margin = new Padding(6, 9, 0, 2);
            l.Font = new Font("Microsoft YaHei UI", 8.5F);
            _buttons.Controls.Add(l);
        }

        Button AddButton(string text, bool accent, EventHandler onClick)
        {
            Button b = new RoundButton();
            b.Text = text;
            b.Width = 214;
            b.Height = accent ? 50 : 38;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = accent ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(58, 62, 78);
            b.BackColor = accent ? Accent : PanelBg;
            b.ForeColor = accent ? Color.White : TextMain;
            b.Font = new Font("Microsoft YaHei UI", accent ? 11F : 9.5F, accent ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            b.Margin = new Padding(6, accent ? 2 : 2, 6, 2);
            b.Click += delegate { if (_busy) { Log("正在执行上一个操作，请稍候……"); return; } onClick(null, EventArgs.Empty); };
            _buttons.Controls.Add(b);
            return b;
        }

        void BuildLog()
        {
            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(8, 6, 16, 6);
            right.BackColor = Bg;
            Controls.Add(right);

            LogBox box = new LogBox();
            box.Dock = DockStyle.Fill;
            box.BackColor = Bg;
            box.Box.BackColor = Bg;
            box.Box.ForeColor = Color.FromArgb(200, 210, 226);
            box.Box.Font = new Font("Microsoft YaHei UI", 10.5F);
            right.Controls.Add(box);
            _log = box.Box;
        }

        void BuildFooter()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Bottom;
            p.Height = 30;
            p.BackColor = PanelBg;
            Controls.Add(p);

            _lblBusy = new Label();
            _lblBusy.Text = "就绪";
            _lblBusy.AutoSize = true;
            _lblBusy.ForeColor = TextDim;
            _lblBusy.Location = new Point(18, 7);
            p.Controls.Add(_lblBusy);
        }

        // ---------- 日志 ----------
        void Log(string msg)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), msg); return; }
            _log.AppendText(msg + "\r\n");
        }

        void SetBusy(bool busy, string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<bool, string>(SetBusy), busy, text); return; }
            _busy = busy;
            _lblBusy.Text = text;
            _lblBusy.ForeColor = busy ? WarnColor : TextDim;
            _buttons.Enabled = true;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        // ---------- 状态检查 ----------
        void RefreshStatus()
        {
            if (_busy) return;
            Task.Run(delegate
            {
                string live = DetectRadminIp();
                bool radmin = !string.IsNullOrEmpty(live);

                if (IsHost)
                {
                    bool proxy = PortOpenAny("127.0.0.1", ChannelPorts, 600);
                    bool server = PortOpenAny(Cfg.VmIp, ChannelPorts, 900) && PortOpen(Cfg.VmIp, 10011, 900);
                    bool same = radmin && live == Cfg.IP;
                    long ping = server ? MeasurePing(Cfg.VmIp, 10011) : -1;
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            SetStatus(_lblRadmin, radmin ? "● Radmin 网卡" : "● Radmin 未连接", radmin);
                            SetStatus(_lblProxy, proxy ? "● 端口转发 开" : "● 端口转发 关", proxy);
                            SetStatus(_lblServer, server ? "● 服务端 运行中" : "● 服务端 未启动", server);
                            SetPing(ping);
                            if (_lblIp != null)
                            {
                                _lblIp.Text = radmin ? live : "Radmin 未连接";
                                _lblIp.ForeColor = radmin ? Accent : BadColor;
                                Panel hp = _lblIp.Parent as Panel;
                                if (hp != null) _lblIp.Left = hp.Width - _lblIp.Width - 26;
                            }
                            if (!radmin) _lblInfo.Text = "请先连上 Radmin VPN";
                            else if (!same) _lblInfo.Text = "IP 与配置不一致（配置 " + Cfg.IP + "）→ 用【修改联机 IP】";
                            else _lblInfo.Text = "对方连接地址：" + live;
                        }));
                    }
                    catch { }
                }
                else
                {
                    string host = Cfg.HostIp;
                    bool haveHost = !string.IsNullOrEmpty(host);
                    bool server = haveHost && PortOpenAny(host, ChannelPorts, 900);
                    long ping = server ? MeasurePing(host, 10011) : -1;
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            SetStatus(_lblRadmin, radmin ? "● Radmin 网卡" : "● Radmin 未连接", radmin);
                            SetStatus(_lblServer, server ? "● 服务端 已开启" : "● 服务端 未开启", server);
                            SetPing(ping);
                            _lblInfo.Text = haveHost ? "房主地址：" + host : "房主地址：未设置";
                            _lblInfo.ForeColor = haveHost ? TextDim : WarnColor;
                            if (_lblIp != null)
                            {
                                _lblIp.Text = radmin ? live : "Radmin 未连接";
                                _lblIp.ForeColor = radmin ? Accent : BadColor;
                                Panel hp = _lblIp.Parent as Panel;
                                if (hp != null) _lblIp.Left = hp.Width - _lblIp.Width - 26;
                            }
                        }));
                    }
                    catch { }
                }
            });
        }

        /// 延迟：先 ICMP，被挡就退回 TCP 连接耗时
        static long MeasurePing(string host, int port)
        {
            if (string.IsNullOrEmpty(host)) return -1;
            try
            {
                using (Ping p = new Ping())
                {
                    PingReply r = p.Send(host, 800);
                    if (r != null && r.Status == IPStatus.Success) return r.RoundtripTime;
                }
            }
            catch { }
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(900)) return -1;
                    c.EndConnect(ar);
                    sw.Stop();
                    return sw.ElapsedMilliseconds;
                }
            }
            catch { return -1; }
        }

        void SetPing(long ms)
        {
            if (_lblPing == null) return;
            if (ms < 0)
            {
                _lblPing.Text = "延迟 --";
                _lblPing.ForeColor = TextDim;
                return;
            }
            _lblPing.Text = "延迟 " + ms + " ms";
            _lblPing.ForeColor = ms < 60 ? OkColor : (ms < 150 ? WarnColor : BadColor);
        }

        /// 枚举网卡，取 Radmin VPN 虚拟网卡上的 IPv4（没有就用 26.x.x.x 兜底）
        internal static string DetectRadminIp()
        {
            try
            {
                string fallback = null;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    string desc = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    bool isRadmin = desc.Contains("radmin");
                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;
                        if (isRadmin) return ip;
                        if (fallback == null && ip.StartsWith("26.")) fallback = ip;
                    }
                }
                return fallback;
            }
            catch { return null; }
        }

        void SetStatus(Label l, string text, bool ok)
        {
            l.Text = text;
            l.ForeColor = ok ? OkColor : BadColor;
        }

        static bool HasLocalIp(string ip)
        {
            try
            {
                IPAddress target = IPAddress.Parse(ip);
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.Equals(target)) return true;
                }
            }
            catch { }
            return false;
        }

        static bool PortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    c.EndConnect(ar);
                    return true;
                }
            }
            catch { return false; }
        }

        // ---------- 外部命令 ----------
        ProcessStartInfo PyPsi(string script, string args, bool capture)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Cfg.Python);
            psi.Arguments = "\"" + Cfg.Script(script) + "\"" + (args.Length > 0 ? " " + args : "");
            psi.WorkingDirectory = Cfg.ToolsDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            // 把路径设置传给 python 脚本，改了文件夹名/移动了位置也能用
            psi.EnvironmentVariables["DNF_VMX"] = Cfg.Vmx;
            psi.EnvironmentVariables["DNF_VMRUN"] = Cfg.VmRun;
            psi.EnvironmentVariables["DNF_CLIENT"] = Cfg.ClientDir;
            psi.EnvironmentVariables["DNF_ROBOTPKG"] = Cfg.RobotPkg;
            psi.EnvironmentVariables["DNF_GAMEDIR"] = Cfg.RemoteGameDir;
            psi.EnvironmentVariables["DNF_SSH_HOST"] = Cfg.VmIp;
            psi.EnvironmentVariables["DNF_SSH_PORT"] = Cfg.SshPort.ToString();
            psi.EnvironmentVariables["DNF_SSH_USER"] = Cfg.SshUser;
            psi.EnvironmentVariables["DNF_SSH_PWD"] = Cfg.SshPwd;
            if (capture)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.GetEncoding(936);
                psi.StandardErrorEncoding = Encoding.GetEncoding(936);
            }
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "gbk";
            psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            return psi;
        }

        /// 同步执行 python 脚本并把输出打到日志里（在后台线程调用）
        int RunScriptSync(string script, string args, string title)
        {
            if (!File.Exists(Cfg.Script(script)))
            {
                Log("[错误] 找不到脚本：" + Cfg.Script(script));
                return -1;
            }
            if (!File.Exists(Cfg.Python))
            {
                Log("[错误] 找不到 Python：" + Cfg.Python);
                Log("        把工具文件夹里的 python 子目录一起复制过去就行，");
                Log("        或者装一个 Python 3 再把 启动器配置.ini 里的 PYTHON= 指向它。");
                return -1;
            }
            Log("──────────────────────────────");
            Log("▶ " + title);
            Log("  python " + script + (args.Length > 0 ? " " + args : ""));
            try
            {
                using (Process p = Process.Start(PyPsi(script, args, true)))
                {
                    p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) Log(e.Data); };
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) Log("[stderr] " + e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    // 加超时：脚本卡住（SSH 无响应）时不能让工具跟着卡死
                    if (!p.WaitForExit(120000))
                    {
                        Log("  [超时] 脚本 120 秒没有结束，强制结束它");
                        try { p.Kill(); } catch { }
                        return -2;
                    }
                    if (p.ExitCode != 0) Log("  [失败] 退出码 " + p.ExitCode);
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Log("[错误] " + ex.Message);
                return -1;
            }
        }

        void RunScript(string script, string args, string title)
        {
            SetBusy(true, "正在执行：" + title);
            Task.Run(delegate
            {
                try { RunScriptSync(script, args, title); }
                finally { SetBusy(false, "就绪"); RefreshStatus(); }
            });
        }

        /// 以管理员身份执行 python 脚本（输出写临时文件再读回来）
        void RunElevatedScript(string script, string args, string title)
        {
            if (!File.Exists(Cfg.Script(script)))
            {
                Log("[错误] 找不到脚本：" + Cfg.Script(script));
                return;
            }
            string tmp = Path.Combine(Path.GetTempPath(), "dnf_launcher_" + Guid.NewGuid().ToString("N") + ".txt");
            string bat = Path.Combine(Path.GetTempPath(), "dnf_launcher_" + Guid.NewGuid().ToString("N") + ".bat");
            Encoding gbk = Encoding.GetEncoding(936);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 936 >nul");
            sb.AppendLine("set PYTHONIOENCODING=gbk");
            sb.AppendLine("set PYTHONUNBUFFERED=1");
            sb.AppendLine("\"" + Cfg.Python + "\" \"" + Cfg.Script(script) + "\" " + args + " > \"" + tmp + "\" 2>&1");
            File.WriteAllText(bat, sb.ToString(), gbk);

            Log("──────────────────────────────");
            Log("▶ " + title + "（需要管理员权限）");
            try
            {
                // 直接用 ShellExecute 的 runas 动词提权，比链一层 powershell 稳定
                ProcessStartInfo psi = new ProcessStartInfo(bat);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = Cfg.ToolsDir;
                using (Process p = Process.Start(psi)) p.WaitForExit();
                if (File.Exists(tmp))
                {
                    foreach (string line in File.ReadAllLines(tmp, gbk)) Log(line);
                    File.Delete(tmp);
                }
                Log("  完成");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) Log("[提示] 你取消了 UAC 授权，这一步没执行。");
                else Log("[错误] 提权失败：" + ex.Message);
            }
            finally
            {
                try { if (File.Exists(bat)) File.Delete(bat); } catch { }
            }
        }

        // ---------- 业务动作 ----------
        void OneClickStart()
        {
            SetBusy(true, IsHost ? "正在一键启动……" : "正在一键设置……");
            Task.Run(delegate
            {
                try
                {
                    Log("");
                    if (IsHost)
                    {
                        Log("════════ 一键启动联机（房主） ════════");
                        RunElevatedScript("dnf_set_hosts.py", Cfg.IP, "① 设置 hosts");
                        string robotArg = Cfg.Robot ? "--robot" : "--no-robot";
                        int rc = RunScriptSync("dnf_server_ctl.py", "start " + robotArg,
                                               Cfg.Robot ? "② 启动假人系统 + 虚拟机 + 服务端" : "② 启动虚拟机与服务端");
                        if (rc != 0) Log("[提示] 服务端启动有问题，继续尝试启动转发器并自检。");
                        RunScriptSync("dnf_proxy_ctl.py", "start", "③ 启动端口转发器");
                        RunScriptSync("dnf_check.py", "", "④ 自检");
                        if (Cfg.Robot) Log("假人管理页：http://" + Cfg.VmIp + ":8112（密码见配置），进游戏后可在里面把 Auto 打开");
                        Log("════════ 启动流程结束 ════════");
                    }
                    else
                    {
                        Log("════════ 一键启动联机（朋友） ════════");
                        if (string.IsNullOrEmpty(Cfg.HostIp))
                        {
                            Log("[错误] 还没设置房主 IP，请先点左侧【房主 IP】填上（或点【自动查找】）。");
                            return;
                        }
                        Log("房主 IP：" + Cfg.HostIp);
                        string backup = Path.Combine(Cfg.ToolsDir, "备份", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                        PatchClient(Path.Combine(Cfg.ClientDir, "DLConfig.ini"), Cfg.HostIp, backup, true);
                        PatchClient(Path.Combine(Cfg.ClientDir, "muqing.ini"), Cfg.HostIp, backup, false);
                        PatchClientRedirect(Path.Combine(Cfg.ClientDir, "DNFServer.ini"), Cfg.HostIp, backup);
                        Log("");
                        Log("√ 客户端配置已指向房主 " + Cfg.HostIp);
                        Log("下一步：点【启动游戏】（会以管理员权限打开登录器）");
                        Log("════════ 设置完成 ════════");
                    }
                }
                finally
                {
                    SetBusy(false, "就绪");
                    RefreshStatus();
                    BeginInvoke(new Action(delegate
                    {
                        DialogResult r = MessageBox.Show(this,
                            IsHost ? "开服流程跑完了。\n\n现在要启动游戏（登录器）吗？"
                                   : "客户端已经改成房主的地址了。\n\n现在要启动游戏（登录器）吗？",
                            "DNF联机工具V1.0", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (r == DialogResult.Yes) StartGame();
                    }));
                }
            });
        }

        // ---------- 假人系统 ----------
        void SetRobotCheck(bool on)
        {
            if (_chkRobot == null) return;
            if (InvokeRequired) { BeginInvoke(new Action(delegate { SetRobotCheck(on); })); return; }
            _chkRobot.Checked = on;
            Cfg.Robot = on;
            Cfg.Save();
        }

        void EnsureRobotDeployed()
        {
            if (Cfg.RobotReady) return;
            DialogResult r = MessageBox.Show(this,
                "第一次在这台电脑开服，先确认一件事：\n\n" +
                "要不要现在把【假人系统（robot）】\n" +
                "一起部署到服务端？\n\n" +
                "· 上传 robot 程序到 /root/robot\n" +
                "· 放好 /root/config 里的配置和密钥\n" +
                "· 密钥同步到游戏服务端和你的客户端\n\n" +
                "选【是】：现在自动部署（约 1~2 分钟），\n" +
                "　　　　完成后自动勾上「启动假人系统」。\n" +
                "选【否】：不再提醒，假人默认关掉，\n" +
                "　　　　以后想用点【重新部署假人】。",
                "DNF联机工具V1.0", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) DeployRobot(false);
            else
            {
                Cfg.RobotReady = true;
                SetRobotCheck(false);
                Log("已跳过假人部署：假人系统默认关闭（想用可以点【重新部署假人】）。");
            }
        }

        void DeployRobot(bool manual)
        {
            if (_busy) { Log("正在执行上一个操作，请稍候……"); return; }

            // 部署前先把要用的信息收齐：服务端信息（host/端口/用户/密码）+ 客户端路径 + 部署包 + 远端游戏目录
            DeploySetupForm setup = new DeploySetupForm();
            if (setup.ShowDialog(this) != DialogResult.OK) { Log("已取消部署假人。"); return; }

            SetBusy(true, "正在部署假人系统……");
            Task.Run(delegate
            {
                try
                {
                    Log("");
                    Log("════════ 部署假人系统（robot） ════════");
                    string args = "deploy --pkg \"" + Cfg.RobotPkg + "\" --client \"" + Cfg.ClientDir + "\"";
                    int rc = RunScriptSync("dnf_robot_deploy.py", args, "部署假人系统");
                    if (rc == 0)
                    {
                        Cfg.RobotReady = true;
                        SetRobotCheck(true);
                        Log("√ 部署完成，已自动勾上「启动假人系统（./robot）」");
                        Log("下一步：点【假人管理】（会自动登录）→ 密钥 → 应用密钥");
                    }
                    else
                    {
                        Log("[失败] 部署没完成（退出码 " + rc + "），看上面的日志。");
                    }
                    Log("════════ 部署流程结束 ════════");
                }
                finally
                {
                    SetBusy(false, "就绪");
                    RefreshStatus();
                }
            });
        }

        void OpenEnvCheck()
        {
            EnvCheckForm f = new EnvCheckForm();
            f.ShowDialog(this);
        }

        void OpenRobotConfig()
        {
            try
            {
                RobotConfigForm f = new RobotConfigForm();
                f.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Log("[错误] 假人参数窗口打不开：" + ex.Message);
            }
        }

        void OpenRobotManager()
        {
            try
            {
                RobotForm f = new RobotForm();
                f.Show(this);
            }
            catch (Exception ex)
            {
                Log("[提示] 内置管理界面打不开（" + ex.Message + "），改用系统浏览器打开。");
                try { Process.Start("http://" + Cfg.VmIp + ":8112"); } catch { }
            }
        }

        void StartGame()
        {
            string exe = Path.Combine(Cfg.ClientDir, Cfg.Launcher);
            if (!File.Exists(exe))
            {
                Log("[错误] 找不到游戏启动器：" + exe + "（点左侧【设置客户端路径】重新选）");
                return;
            }
            try
            {
                // 登录器要写 hosts（AutoHosts），必须以管理员身份启动，否则界面会白屏
                ProcessStartInfo psi = new ProcessStartInfo(exe);
                psi.WorkingDirectory = Cfg.ClientDir;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                Log("已以管理员身份启动登录器。");
                Log("看到「服务端已上线」后点『登录』，进频道后点『游戏开始』。");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) Log("[提示] 你取消了 UAC 授权，登录器没启动。");
                else Log("[错误] 启动登录器失败：" + ex.Message);
            }
            catch (Exception ex)
            {
                Log("[错误] 启动登录器失败：" + ex.Message);
            }
            finally
            {
                // 点「启动游戏」后工具自动缩到托盘
                BeginInvoke(new Action(HideToTray));
            }
        }

        void SetHosts()
        {
            RunElevatedScript("dnf_set_hosts.py", Cfg.IP, "设置 hosts → " + Cfg.IP);
        }

        void ChangeIp()
        {
            string live = DetectRadminIp();
            string def = !string.IsNullOrEmpty(live) ? live : Cfg.IP;
            string ip = InputBox("修改联机 IP",
                "输入新的 Radmin IP（改完会自动更新服务端 37 个配置、客户端 ini 和 hosts，并重启服务端）：",
                def);
            if (ip == null) return;
            ip = ip.Trim();
            if (!IsIp(ip))
            {
                MessageBox.Show("IP 格式不对：" + ip, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string old = Cfg.IP;
            Cfg.IP = ip;
            Cfg.Save();
            Log("联机 IP：" + old + " → " + ip);

            SetBusy(true, "正在修改联机 IP……");
            Task.Run(delegate
            {
                try
                {
                    RunScriptSync("dnf_set_server_ip.py", ip, "① 修改服务端配置并重启");
                    RunScriptSync("dnf_set_ip.py", "\"" + Cfg.IP + "\" \"" + Cfg.ClientDir + "\"", "② 修改客户端配置");
                    RunElevatedScript("dnf_set_hosts.py", ip, "③ 修改 hosts");
                    Log("");
                    Log("完成：把这台电脑的新 IP " + ip + " 告诉朋友，让他同步改客户端配置。");
                }
                finally
                {
                    SetBusy(false, "就绪");
                    RefreshStatus();
                }
            });
        }

        // ---------- 托盘 ----------
        void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("打开 DNF联机工具", null, delegate { ShowFromTray(); });
            menu.Items.Add(new ToolStripSeparator());
            if (IsHost)
                menu.Items.Add("退出（并停止服务端、端口转发）", null, delegate { ExitAndStop(); });
            else
                menu.Items.Add("退出", null, delegate { ExitApp(); });

            _tray = new NotifyIcon();
            try { _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { _tray.Icon = SystemIcons.Application; }
            _tray.Text = "DNF联机工具V1.0";
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowFromTray(); };
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_reallyExit) return;
            // 点右上角 X 不退出，最小化到托盘
            e.Cancel = true;
            HideToTray();
        }

        void HideToTray()
        {
            if (!Visible) return;
            Hide();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (_tray != null)
            {
                _tray.BalloonTipTitle = "DNF联机工具V1.0";
                _tray.BalloonTipText = "已最小化到托盘。右键图标可以「打开」或「退出」。";
                try { _tray.ShowBalloonTip(2500); } catch { }
            }
        }

        void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        /// 单实例：后台等「别人的第二次启动」发来的显示请求
        public void StartShowListener()
        {
            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            }
            catch (Exception ex)
            {
                Log("[提示] 单实例通知初始化失败：" + ex.Message);
                return;
            }
            Thread t = new Thread(delegate ()
            {
                while (true)
                {
                    try
                    {
                        _showEvent.WaitOne();
                        try { BeginInvoke(new Action(ShowFromTray)); }
                        catch { return; }
                    }
                    catch { return; }
                }
            });
            t.IsBackground = true;
            t.Name = "show-listener";
            t.Start();
        }

        void ExitAndStop()
        {
            if (_busy)
            {
                MessageBox.Show("还有操作正在执行，稍等几秒再退出。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ShowFromTray();
            SetBusy(true, "正在退出（先停服务端，最多约 30 秒）……");
            Task.Run(delegate
            {
                try
                {
                    bool proxyUp = PortOpenAny("127.0.0.1", ChannelPorts, 500);
                    bool serverUp = PortOpenAny(Cfg.VmIp, ChannelPorts, 800) || PortOpen(Cfg.VmIp, 10011, 800);
                    Log("");
                    if (!proxyUp && !serverUp)
                    {
                        Log("════════ 退出：服务端和端口转发都没开，直接退出 ════════");
                        return;
                    }
                    Log("════════ 退出：先停止服务端与端口转发 ════════");
                    if (serverUp)
                    {
                        RunScriptSync("dnf_server_ctl.py", "stop", "停止服务端");
                        Log("  等待服务端端口关闭……");
                        WaitPortClosed(Cfg.VmIp, 10011, 25);
                    }
                    if (proxyUp)
                    {
                        RunScriptSync("dnf_proxy_ctl.py", "stop", "停止端口转发");
                        Log("  等待端口转发关闭……");
                        WaitPortsClosed("127.0.0.1", ChannelPorts, 20);
                    }
                    Log("服务端与端口转发已全部关闭。");
                }
                catch (Exception ex)
                {
                    Log("[错误] 退出时清理失败：" + ex.Message);
                }
                finally
                {
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            _timer.Stop();
                            _reallyExit = true;
                            if (_tray != null) _tray.Visible = false;
                            Application.Exit();
                        }));
                    }
                    catch { }
                }
            });
        }

        /// 轮询等待端口关闭，最多等 seconds 秒
        static void WaitPortClosed(string host, int port, int seconds)
        {
            for (int i = 0; i < seconds; i++)
            {
                if (!PortOpen(host, port, 400)) return;
                Thread.Sleep(1000);
            }
        }

        // 85A 的频道端口是 7101，70 版是 7001 —— 两个都试，谁在听算谁
        static readonly int[] ChannelPorts = new int[] { 7101, 7001 };

        static bool PortOpenAny(string host, int[] ports, int timeoutPerPort)
        {
            foreach (int pt in ports) if (PortOpen(host, pt, timeoutPerPort)) return true;
            return false;
        }

        static void WaitPortsClosed(string host, int[] ports, int seconds)
        {
            for (int i = 0; i < seconds; i++)
            {
                if (!PortOpenAny(host, ports, 400)) return;
                Thread.Sleep(1000);
            }
        }

        /// 朋友端：直接退出（不用停任何服务）
        void ExitApp()
        {
            _timer.Stop();
            _reallyExit = true;
            if (_tray != null) _tray.Visible = false;
            Application.Exit();
        }

        // ---------- 朋友端：房主 IP / 客户端配置 ----------
        void SetHostIp()
        {
            string ip = InputBox("房主 IP",
                "输入房主的 Radmin IP（就是房主端右上角那串数字）。\n不清楚的话直接留空点确定，我来在 Radmin 网段里自动找：",
                Cfg.HostIp);
            if (ip == null) return;
            ip = ip.Trim();
            if (ip.Length == 0) { AutoDetectHost(); return; }
            if (!IsIp(ip))
            {
                MessageBox.Show("IP 格式不对：" + ip, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cfg.HostIp = ip;
            Cfg.Save();
            RefreshButtons();
            RefreshStatus();
            Log("");
            Log("√ 房主 IP 已保存：" + ip);
        }

        void AutoDetectHost()
        {
            if (_busy) { Log("正在执行上一个操作，请稍候……"); return; }
            SetBusy(true, "正在自动查找房主……");
            Task.Run(delegate
            {
                try
                {
                    Log("");
                    Log("▶ 自动查找房主：扫本地 Radmin 网段的频道端口（7101 / 7001）……");
                    if (string.IsNullOrEmpty(DetectRadminIp()))
                    {
                        Log("[错误] 没找到 Radmin 网卡，请先连上 Radmin VPN。");
                        return;
                    }
                    string found = ScanHost(1200);
                    if (found == null)
                    {
                        Log("[错误] 没扫到房主。请让房主先【一键启动联机】，或点【房主 IP】手动填地址。");
                        return;
                    }
                    Cfg.HostIp = found;
                    Cfg.Save();
                    Log("√ 找到房主：" + found);
                }
                finally
                {
                    SetBusy(false, "就绪");
                    RefreshButtons();
                    RefreshStatus();
                }
            });
        }

        /// 在本地 Radmin 网段 /24 上扫 7001，返回第一个能连上的地址
        static string ScanHost(int timeoutMs)
        {
            string live = DetectRadminIp();
            if (string.IsNullOrEmpty(live)) return null;
            string[] parts = live.Split('.');
            if (parts.Length != 4) return null;
            string prefix = parts[0] + "." + parts[1] + "." + parts[2] + ".";

            string[] box = new string[1];
            ManualResetEvent done = new ManualResetEvent(false);
            int pending = 255;
            for (int i = 1; i <= 254; i++)
            {
                string ip = prefix + i;
                if (ip == live)
                {
                    if (Interlocked.Decrement(ref pending) == 0) done.Set();
                    continue;
                }
                ThreadPool.QueueUserWorkItem(delegate(object st)
                {
                    try
                    {
                        string target = (string)st;
                        if (box[0] == null && PortOpenAny(target, ChannelPorts, timeoutMs))
                        {
                            Interlocked.CompareExchange(ref box[0], target, null);
                            done.Set();
                        }
                    }
                    finally
                    {
                        if (Interlocked.Decrement(ref pending) == 0) done.Set();
                    }
                }, ip);
            }
            done.WaitOne(timeoutMs + 3000, false);
            done.Close();
            return box[0];
        }

        void CheckFriendStatus()
        {
            if (_busy) { Log("正在执行上一个操作，请稍候……"); return; }
            SetBusy(true, "正在检查……");
            Task.Run(delegate
            {
                try
                {
                    Log("");
                    Log("════════ DNF 联机（朋友端）状态自检 ════════");
                    int bad = 0;

                    bool clientOk = !string.IsNullOrEmpty(Cfg.ClientDir) && File.Exists(Path.Combine(Cfg.ClientDir, Cfg.Launcher));
                    Log((clientOk ? "[√] " : "[×] ") + "游戏路径：" + (string.IsNullOrEmpty(Cfg.ClientDir) ? "未设置" : Path.Combine(Cfg.ClientDir, Cfg.Launcher)));
                    if (!clientOk) bad++;

                    string live = DetectRadminIp();
                    bool radmin = !string.IsNullOrEmpty(live);
                    Log((radmin ? "[√] " : "[×] ") + (radmin ? "Radmin 网卡在线，本机地址 " + live : "没找到 Radmin 网卡（请先连上 Radmin VPN）"));
                    if (!radmin) bad++;

                    bool haveHost = !string.IsNullOrEmpty(Cfg.HostIp);
                    Log((haveHost ? "[√] " : "[×] ") + "房主地址：" + (haveHost ? Cfg.HostIp : "未设置（点左侧【房主 IP】）"));
                    if (!haveHost) bad++;

                    bool server = haveHost && PortOpenAny(Cfg.HostIp, ChannelPorts, 1200);
                    Log((server ? "[√] " : "[×] ") + (server ? "房主服务端已开启：频道端口 7101/7001 能连上" : "连不上房主的频道端口 7101/7001（房主没开服，或者没连上他的 Radmin）"));
                    if (!server) bad++;

                    long ms = server ? MeasurePing(Cfg.HostIp, 10011) : -1;
                    Log(ms >= 0 ? "[√] 和房主的延迟：" + ms + " ms（越小越顺畅）" : "[×] 和房主的延迟：测不出来");
                    if (ms < 0) bad++;

                    if (clientOk && haveHost)
                    {
                        string db = ReadIniValue(Path.Combine(Cfg.ClientDir, "DLConfig.ini"), "数据库信息", "DBHost");
                        string gh = ReadIniValue(Path.Combine(Cfg.ClientDir, "DLConfig.ini"), "登录器信息", "GameHost");
                        string muqingPath = Path.Combine(Cfg.ClientDir, "muqing.ini");
                        string mu = File.Exists(muqingPath) ? ReadIniValue(muqingPath, "登录配置", "服务器IP") : null;
                        string dnfSrvPath = Path.Combine(Cfg.ClientDir, "DNFServer.ini");
                        string rip = File.Exists(dnfSrvPath) ? ReadIniValue(dnfSrvPath, "Network", "RedirectIP") : null;
                        bool okDb = db == Cfg.HostIp && gh == Cfg.HostIp;
                        bool okMu = (mu == null) || mu == Cfg.HostIp;
                        bool okRip = (rip == null) || rip == Cfg.HostIp;
                        bool ok = okDb && okMu && okRip;
                        Log((ok ? "[√] " : "[×] ") + (ok
                            ? "客户端配置：DLConfig.ini(DBHost/GameHost)" + (mu == null ? "" : "、muqing.ini(服务器IP)") + (rip == null ? "" : "、DNFServer.ini(RedirectIP)") + " 都指向房主"
                            : "客户端配置没指向房主（点【一键启动联机】重跑一次）：DBHost=" + db + "，GameHost=" + gh
                              + (mu == null ? "" : "，muqing服务器IP=" + mu)
                              + (rip == null ? "" : "，DNFServer RedirectIP=" + rip)));
                        if (!ok) bad++;

                        // ---- 版本一致性（客户端本体是否和房主同一个版本）----
                        string dnfExe = Path.Combine(Cfg.ClientDir, "DNF.exe");
                        string expectMd5 = IniValue("DNFEXE_MD5", "d36ed8ad200a2b4e87d1269c3adc993b");
                        long expectSize = 0;
                        long.TryParse(IniValue("DNFEXE_SIZE", "55541248"), out expectSize);
                        if (!File.Exists(dnfExe))
                        {
                            Log("[×] 版本一致性：客户端目录里没有 DNF.exe，路径可能选错了");
                            bad++;
                        }
                        else
                        {
                            long sz = new FileInfo(dnfExe).Length;
                            string md5 = Md5OfFile(dnfExe);
                            bool verOk = (sz == expectSize) && (string.Equals(md5, expectMd5, StringComparison.OrdinalIgnoreCase));
                            if (verOk)
                            {
                                Log("[√] 版本一致性：DNF.exe " + sz + " 字节，MD5 与房主一致");
                            }
                            else
                            {
                                Log("[×] 版本不一致！DNF.exe 大小 " + sz + "（应是 " + expectSize + "），MD5 " + md5.Substring(0, 8) + "…（应是 " + expectMd5.Substring(0, 8) + "…）");
                                Log("        请换成和房主完全相同的客户端（整个 ImagePacks2 也要一致），否则进不了频道");
                                bad++;
                            }
                        }
                        string gameVer = ReadIniValue(Path.Combine(Cfg.ClientDir, "DLConfig.ini"), "登录器信息", "GameVer");
                        if (!string.IsNullOrEmpty(gameVer))
                        {
                            string norm = gameVer.Replace(" ", "");
                            bool gvOk = norm.StartsWith("2,130,8,1");
                            Log((gvOk ? "[√] " : "[×] ") + "登录器版本号 GameVer=" + gameVer + (gvOk ? "" : "（房主是 2, 130, 8, 1）"));
                            if (!gvOk) bad++;
                        }
                        string[] pems = new string[] { "publickey.pem", "privatekey.pem" };
                        int missPem = 0;
                        foreach (string pem in pems) if (!File.Exists(Path.Combine(Cfg.ClientDir, pem))) missPem++;
                        if (missPem > 0)
                        {
                            Log("[×] 客户端缺少 " + missPem + " 个密钥文件（publickey.pem / privatekey.pem）");
                            Log("        这两个文件必须和房主的一致，否则选完频道会卡在「正在连线伺服器」");
                            Log("        做法：把房主客户端目录里的 publickey.pem、privatekey.pem 拷过来");
                            bad++;
                        }
                    }

                    bool hostsOk = haveHost && HasHostsLine(Cfg.HostIp);
                    Log((hostsOk ? "[√] " : "[ ] ") + (hostsOk
                        ? "hosts：" + Cfg.HostIp + " start.dnf.tw"
                        : "hosts 里暂时没有这行（不用管，点【启动游戏】时登录器会自己写）"));

                    Log("----------------------------------------------");
                    if (bad == 0)
                    {
                        Log("全部正常，可以进游戏了！");
                        Log("下一步：点【启动游戏】，等登录器显示「服务端已上线」后点【登录】。");
                    }
                    else
                    {
                        Log("有 " + bad + " 项没通过，按上面的提示处理后重新检查一次。");
                    }
                }
                finally
                {
                    SetBusy(false, "就绪");
                    RefreshStatus();
                }
            });
        }

        /// 读 GBK ini 的某个值
        static string ReadIniValue(string file, string section, string key)
        {
            try
            {
                if (!File.Exists(file)) return null;
                Encoding gbk = Encoding.GetEncoding(936);
                string cur = "";
                foreach (string raw in File.ReadAllLines(file, gbk))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        cur = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }
                    if (cur != section) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (line.Substring(0, eq).Trim() == key) return line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return null;
        }

        static bool HasHostsLine(string ip)
        {
            try
            {
                if (string.IsNullOrEmpty(ip)) return false;
                string hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
                if (!File.Exists(hosts)) return false;
                foreach (string raw in File.ReadAllLines(hosts))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (line.Contains("start.dnf.tw") && line.Contains(ip)) return true;
                }
            }
            catch { }
            return false;
        }

        /// 把客户端 ini 里的房主 IP 改成 host（改前备份）
        void PatchClient(string file, string host, string backupDir, bool isDlConfig)
        {
            if (!File.Exists(file))
            {
                Log("[错误] 找不到文件：" + file);
                return;
            }
            string name = Path.GetFileName(file);
            try
            {
                Directory.CreateDirectory(backupDir);
                File.Copy(file, Path.Combine(backupDir, name), true);
            }
            catch { }
            Encoding gbk = Encoding.GetEncoding(936);
            string[] lines = File.ReadAllLines(file, gbk);
            string cur = "";
            int changed = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string t = line.Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    cur = t.Substring(1, t.Length - 2).Trim();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string old = line.Substring(eq + 1).Trim();
                bool hit;
                if (isDlConfig) hit = (cur == "数据库信息" && key == "DBHost") || (cur == "登录器信息" && key == "GameHost");
                else hit = (cur == "登录配置" && key == "服务器IP");
                if (!hit) continue;
                if (old == host)
                {
                    Log("  " + name + "：" + key + " 已经是 " + host);
                    continue;
                }
                lines[i] = key + "=" + host;
                changed++;
                Log("  " + name + "：" + key + " " + old + " → " + host);
            }
            File.WriteAllLines(file, lines, gbk);
            Log("  [备份] " + backupDir);
            if (changed == 0) Log("  （" + name + " 不用改）");
        }

        /// <summary>85A 客户端专有：DNFServer.ini 的 [Network] RedirectIP 会绕过 hosts 直连，
        /// 朋友端必须改成房主 IP，否则永远连到 192.168.200.131（局域网）失败。</summary>
        void PatchClientRedirect(string file, string host, string backupDir)
        {
            if (!File.Exists(file))
            {
                Log("  （没有 DNFServer.ini，跳过：老版本客户端不需要）");
                return;
            }
            string name = Path.GetFileName(file);
            try
            {
                Directory.CreateDirectory(backupDir);
                File.Copy(file, Path.Combine(backupDir, name), true);
            }
            catch { }
            Encoding gbk = Encoding.GetEncoding(936);
            string[] lines = File.ReadAllLines(file, gbk);
            string cur = "";
            int changed = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string t = line.Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    cur = t.Substring(1, t.Length - 2).Trim();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                if (key != "RedirectIP") continue;
                string old = line.Substring(eq + 1).Trim();
                if (old == host)
                {
                    Log("  " + name + "：RedirectIP 已经是 " + host);
                    continue;
                }
                lines[i] = key + "=" + host;
                changed++;
                Log("  " + name + "：RedirectIP " + old + " → " + host);
            }
            File.WriteAllLines(file, lines, gbk);
            if (changed == 0) Log("  （" + name + " 不用改）");
        }

        static string Md5OfFile(string path)
        {
            try
            {
                using (MD5 md5 = MD5.Create())
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] h = md5.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in h) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }

        static string IniValue(string key, string fallback)
        {
            try
            {
                string ini = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "启动器配置.ini");
                if (File.Exists(ini))
                {
                    foreach (string line in File.ReadAllLines(ini, Encoding.UTF8))
                    {
                        string t = line.Trim();
                        if (t.StartsWith("#")) continue;
                        int eq = t.IndexOf('=');
                        if (eq <= 0) continue;
                        if (t.Substring(0, eq).Trim() == key) return t.Substring(eq + 1).Trim();
                    }
                }
            }
            catch { }
            return fallback;
        }

        // ---------- 路径设置 ----------
        void CheckPaths()
        {
            bool bad = false;
            if (!Directory.Exists(Cfg.ClientDir) || !File.Exists(Path.Combine(Cfg.ClientDir, Cfg.Launcher)))
            {
                Log("");
                Log("[警告] 客户端路径或游戏启动器无效：" + Path.Combine(Cfg.ClientDir, Cfg.Launcher));
                Log("        请点左侧【设置客户端路径】重新选客户端目录里的游戏启动器。");
                bad = true;
            }
            if (!File.Exists(Cfg.Vmx))
            {
                Log("");
                Log("[警告] 服务端虚拟机路径无效：" + Cfg.Vmx);
                Log("        请点左侧【设置服务端路径】重新选 CentOS 7.6.vmx（可能在移动过文件夹之后失效）。");
                bad = true;
            }
            if (bad) Log("");
        }

        void SetClientPath()
        {
            Log("");
            Log("▶ 设置游戏启动器（当前：" + Path.Combine(Cfg.ClientDir, Cfg.Launcher) + "）");
            string initDir = Cfg.ClientDir;
            if (string.IsNullOrEmpty(initDir) || !Directory.Exists(initDir)) initDir = Cfg.ExeDir;
            using (OpenFileDialog o = new OpenFileDialog())
            {
                o.Title = "选择客户端目录里的游戏启动器";
                o.Filter = "游戏启动器 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                o.InitialDirectory = initDir;
                if (o.ShowDialog(this) != DialogResult.OK)
                {
                    Log("  已取消。");
                    return;
                }
                string p = o.FileName;
                Cfg.ClientDir = Path.GetDirectoryName(p);
                Cfg.Launcher = Path.GetFileName(p);
                Cfg.Save();
                Log("√ 客户端路径已更新：" + Cfg.ClientDir);
                Log("  游戏启动器：" + Cfg.Launcher);
            }
        }

        void SetServerPath()
        {
            Log("");
            Log("▶ 设置服务端路径（当前：" + Cfg.Vmx + "）");
            string initDir = "";
            try { initDir = Path.GetDirectoryName(Cfg.Vmx); } catch { }
            if (string.IsNullOrEmpty(initDir) || !Directory.Exists(initDir)) initDir = @"E:\";
            using (OpenFileDialog o = new OpenFileDialog())
            {
                o.Title = "选择服务端虚拟机配置文件（.vmx）";
                o.Filter = "VMware 虚拟机配置 (*.vmx)|*.vmx|所有文件 (*.*)|*.*";
                o.InitialDirectory = initDir;
                if (o.ShowDialog(this) != DialogResult.OK)
                {
                    Log("  已取消。");
                    return;
                }
                string p = o.FileName;
                Cfg.Vmx = p;
                Cfg.Save();
                Log("√ 服务端路径已更新：" + p);
            }
        }

        // ---------- 教程 ----------
        void ShowTutorial()
        {
            _log.Clear();
            foreach (string line in TutorialText()) _log.AppendText(line + Environment.NewLine);
            _log.SelectionStart = 0;
            _log.ScrollToCaret();
        }

        static string[] TutorialText()
        {
            return new string[]
            {
                "════════ 房主 · 三步开服 ════════",
                "",
                "【第 1 步】连上网络",
                "  打开 Radmin VPN，加入你们约好的网络。",
                "  右上角出现 26.x.x.x 才算连上。",
                "",
                "【第 2 步】一键启动",
                "  点左边蓝色【一键启动联机】→ 弹窗问「要启动游戏吗」选【是】。",
                "  它会自己做完五件事（日志里逐行能看到）：",
                "    hosts → 假人(./robot) → 服务端 → 端口转发 → 自检",
                "  冷启动约 40 秒，等五步都打勾就行。",
                "",
                "【第 3 步】进游戏",
                "  登录器显示「服务端已上线」→ 点【登录】→ 选频道 →【游戏开始】。",
                "",
                "发给朋友两样东西：① 你的 Radmin IP ② 这个 exe（他选「我不是房主」）。",
                "",
                "【其它】",
                "  · 假人：左上角勾选框控制要不要带；【假人管理】可以看人数、应用密钥（自动登录）。",
                "  · 不玩了：点左下角【退出】，会先停服务端和转发再关掉工具。",
                "  · 卡住或报错：点【一键检查状态】，哪项红就看日志对应那行。",
                ""
            };
        }

        void ShowFriendTutorial()
        {
            _log.Clear();
            foreach (string line in FriendTutorialText()) _log.AppendText(line + Environment.NewLine);
            _log.SelectionStart = 0;
            _log.ScrollToCaret();
        }

        static string[] FriendTutorialText()
        {
            return new string[]
            {
                "════════ 朋友 · 三步进游戏 ════════",
                "",
                "【第 1 步】连上房主的网络",
                "  装 Radmin VPN →「加入网络」→ 输入房主给你的网络名和密码。",
                "  左上角「Radmin 网卡」变绿就是连上了。",
                "",
                "【第 2 步】告诉工具你的游戏在哪",
                "  点【游戏路径】→ 选你客户端目录里的登录器 exe。",
                "  点【房主 IP】→ 填房主给你的 26.x.x.x。",
                "  （不知道就留空点确定，工具会在 Radmin 网段里自动找）",
                "",
                "【第 3 步】一键启动",
                "  点【一键启动联机】→ 自动把客户端配置改成房主的地址 →",
                "  弹窗问「现在启动游戏吗」选【是】。",
                "  登录器显示「服务端已上线」就是连上了，账号密码问房主要。",
                "",
                "【连不上怎么办】",
                "  点【一键检查状态】，红色的那项按提示处理：",
                "  · 房主地址没填 → 点【房主 IP】",
                "  · 频道端口（7101/7001）连不上 → 房主没开服，或你没连上他的 Radmin",
                "  · 配置没指向房主 → 再点一次【一键启动联机】",
                "  · 登录器白屏 → 一定要用工具里的【启动游戏】（自动提权）",
                ""
            };
        }

        static bool IsIp(string s)
        {
            IPAddress a;
            return IPAddress.TryParse(s, out a) && s.Split('.').Length == 4;
        }

        void OpenFile(string path)
        {
            if (!File.Exists(path))
            {
                Log("[错误] 文件不存在：" + path);
                return;
            }
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch
            {
                try { Shell("notepad.exe", "\"" + path + "\""); } catch { }
            }
        }

        void Shell(string file, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(file, args);
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        // 简易输入框
        string InputBox(string title, string prompt, string def)
        {
            Form f = new Form();
            f.Text = title;
            f.ClientSize = new Size(560, 190);
            f.StartPosition = FormStartPosition.CenterParent;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.BackColor = Bg;
            f.ForeColor = TextMain;
            f.Font = new Font("Microsoft YaHei UI", 9.5F);

            Label l = new Label();
            l.Text = prompt;
            l.Location = new Point(20, 18);
            l.Size = new Size(520, 46);
            l.ForeColor = TextMain;
            f.Controls.Add(l);

            TextBox tb = new TextBox();
            tb.Text = def;
            tb.Location = new Point(22, 72);
            tb.Width = 512;
            tb.BackColor = Color.FromArgb(16, 18, 24);
            tb.ForeColor = TextMain;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Font = new Font("Consolas", 11F);
            f.Controls.Add(tb);

            Button ok = new RoundButton();
            ok.Text = "确定";
            ok.Location = new Point(360, 128);
            ok.Size = new Size(82, 32);
            ok.BackColor = Accent;
            ok.ForeColor = Color.White;
            ok.FlatStyle = FlatStyle.Flat;
            ok.FlatAppearance.BorderSize = 0;
            ok.DialogResult = DialogResult.OK;
            f.Controls.Add(ok);

            Button cancel = new RoundButton();
            cancel.Text = "取消";
            cancel.Location = new Point(452, 128);
            cancel.Size = new Size(82, 32);
            cancel.BackColor = PanelBg;
            cancel.ForeColor = TextMain;
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderColor = Color.FromArgb(58, 62, 78);
            cancel.DialogResult = DialogResult.Cancel;
            f.Controls.Add(cancel);

            f.AcceptButton = ok;
            f.CancelButton = cancel;
            return f.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
        }
    }

    static class Program
    {
        public const string MutexName = "DNF联机工具V1.0_单实例";
        public const string ShowEventName = "DNF联机工具V1.0_显示窗口";

        [STAThread]
        static void Main()
        {
            bool created;
            Mutex mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                // 已经有实例在跑（可能缩在托盘里）：让它把窗口弹出来，然后自己退出
                try
                {
                    EventWaitHandle ev = EventWaitHandle.OpenExisting(ShowEventName);
                    ev.Set();
                    ev.Close();
                }
                catch { }
                MessageBox.Show(
                    "DNF联机工具V1.0 已经在运行了，不能重复打开。\n\n已经帮你把主界面调出来了。\n如果屏幕上没看到，请检查右下角托盘（任务栏「^」里那个红色 DNF 图标），\n双击它或右键【打开】即可回到主界面。",
                    "DNF联机工具V1.0",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                GC.KeepAlive(mutex);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Cfg.Init();

            // 先选身份：我是房主 / 我不是房主
            bool isHost;
            using (RoleForm rf = new RoleForm())
            {
                if (rf.ShowDialog() != DialogResult.OK)
                {
                    GC.KeepAlive(mutex);
                    return;
                }
                isHost = rf.IsHost;
            }

            MainForm form = new MainForm(isHost);
            Current = form;
            form.StartShowListener();
            Application.Run(form);
            GC.KeepAlive(mutex);
        }

        /// 当前主窗口（第二次启动时用它把界面弹出来）
        public static MainForm Current;
    }

    /// 假人管理（把 robot 的 Web 管理页 http://虚拟机IP:8112 嵌进来）
    /// 假人 Web 管理口（8112）的轻量客户端：登录拿 Cookie，再用 /api/call 调命令。
    public static class RobotApi
    {
        /// 跑一次 scripts 里的脚本（供各窗口共用），返回 (退出码, 输出)
        internal static int RunScript(string scriptName, string args, out string output)
        {
            string script = Cfg.Script(scriptName);
            output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Cfg.Python;
                psi.Arguments = "\"" + script + "\" " + args;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.GetEncoding(936);
                psi.StandardErrorEncoding = Encoding.GetEncoding(936);
                psi.WorkingDirectory = Cfg.ToolsDir;
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "gbk";
                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                psi.EnvironmentVariables["DNF_SSH_HOST"] = Cfg.VmIp;
                psi.EnvironmentVariables["DNF_SSH_PORT"] = Cfg.SshPort.ToString();
                psi.EnvironmentVariables["DNF_SSH_USER"] = Cfg.SshUser;
                psi.EnvironmentVariables["DNF_SSH_PWD"] = Cfg.SshPwd;
                using (Process p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(240000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }

        static readonly CookieContainer _jar = new CookieContainer();
        static DateTime _loginAt = DateTime.MinValue;
        static readonly object _lock = new object();
        public static readonly JavaScriptSerializer Js = new JavaScriptSerializer();

        static RobotApi() { Js.MaxJsonLength = int.MaxValue; }

        public static string Base { get { return "http://" + Cfg.VmIp + ":8112"; } }

        public static bool Login(bool force)
        {
            lock (_lock)
            {
                if (!force && _loginAt != DateTime.MinValue && (DateTime.Now - _loginAt).TotalHours < 10) return true;
                try
                {
                    string body = "password=" + Uri.EscapeDataString(Cfg.RobotWebPwd ?? "");
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Base + "/login");
                    req.Method = "POST";
                    req.ContentType = "application/x-www-form-urlencoded";
                    req.CookieContainer = _jar;
                    req.AllowAutoRedirect = false;
                    req.Timeout = 8000;
                    req.ReadWriteTimeout = 8000;
                    byte[] data = Encoding.UTF8.GetBytes(body);
                    req.ContentLength = data.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        int code = (int)resp.StatusCode;
                        if (code != 200 && code != 302 && code != 303) return false;
                    }
                    _loginAt = DateTime.Now;
                    return true;
                }
                catch { return false; }
            }
        }

        public static void Reset() { lock (_lock) { _loginAt = DateTime.MinValue; } }

        /// 调一次 /api/call，成功返回 result 字典
        public static Dictionary<string, object> Call(string command, Dictionary<string, object> payload, out string error, int timeoutMs)
        {
            error = "";
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!Login(attempt > 0)) { error = "连不上假人管理页，服务端是不是没开？"; return null; }
                try
                {
                    if (payload == null) payload = new Dictionary<string, object>();
                    Dictionary<string, object> body = new Dictionary<string, object>();
                    body["command"] = command;
                    body["payload"] = payload;
                    string json = Js.Serialize(body);

                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Base + "/api/call");
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    req.CookieContainer = _jar;
                    req.Timeout = timeoutMs;
                    req.ReadWriteTimeout = timeoutMs;
                    byte[] data = Encoding.UTF8.GetBytes(json);
                    req.ContentLength = data.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                    string text;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        text = sr.ReadToEnd();

                    Dictionary<string, object> root = Js.Deserialize<Dictionary<string, object>>(text);
                    if (root == null) { error = "假人返回内容解析失败"; return null; }
                    object okObj;
                    bool ok = root.TryGetValue("ok", out okObj) && okObj is bool && (bool)okObj;
                    Dictionary<string, object> env = null;
                    object envObj;
                    if (root.TryGetValue("result", out envObj)) env = envObj as Dictionary<string, object>;
                    if (!ok)
                    {
                        error = EnvelopeError(root) ?? "命令执行失败";
                        if (LooksLikeAuth(error)) { Reset(); continue; }
                        return null;
                    }
                    if (env == null) return new Dictionary<string, object>();
                    // 内层还是一层 {ok, result}
                    object innerOk;
                    if (env.TryGetValue("ok", out innerOk) && innerOk is bool)
                    {
                        if (!(bool)innerOk)
                        {
                            error = EnvelopeError(env) ?? "命令执行失败";
                            if (LooksLikeAuth(error)) { Reset(); continue; }
                            return null;
                        }
                        object innerRes;
                        if (env.TryGetValue("result", out innerRes) && innerRes != null)
                        {
                            Dictionary<string, object> d2 = innerRes as Dictionary<string, object>;
                            if (d2 != null) return d2;
                            List<object> lst = innerRes as List<object>;
                            if (lst != null) { Dictionary<string, object> wrap = new Dictionary<string, object>(); wrap["items"] = lst; return wrap; }
                        }
                        return new Dictionary<string, object>();
                    }
                    return env;
                }
                catch (WebException wex)
                {
                    HttpWebResponse r = wex.Response as HttpWebResponse;
                    if (r != null && (int)r.StatusCode == 401) { Reset(); continue; }
                    error = "调用失败：" + wex.Message;
                    return null;
                }
                catch (Exception ex) { error = "调用失败：" + ex.Message; return null; }
            }
            return null;
        }

        public static Dictionary<string, object> Call(string command) { string e; return Call(command, null, out e, 20000); }

        /// GET 一个接口，返回原始文本（用于 /api/game-port 之类）
        public static string Get(string path, out string error)
        {
            error = "";
            if (!Login(false)) { error = "未登录"; return null; }
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Base + path);
                req.Method = "GET";
                req.CookieContainer = _jar;
                req.Timeout = 10000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        /// POST /api/restart-robot（重启假人进程）
        public static bool RestartRobot(out string error)
        {
            error = "";
            if (!Login(false)) { error = "未登录"; return false; }
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Base + "/api/restart-robot");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.CookieContainer = _jar;
                req.Timeout = 30000;
                byte[] data = Encoding.UTF8.GetBytes("{}");
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string t = sr.ReadToEnd();
                    Dictionary<string, object> root = Js.Deserialize<Dictionary<string, object>>(t);
                    object ok;
                    if (root != null && root.TryGetValue("ok", out ok) && ok is bool && (bool)ok) return true;
                    error = t;
                    return false;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// 下载内置默认密钥对 zip，解到指定目录；返回解出的文件列表
        public static string[] DownloadKeypair(string dir, out string error)
        {
            error = "";
            if (!Login(false)) { error = "未登录"; return null; }
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Base + "/api/keypair-download");
                req.Method = "GET";
                req.CookieContainer = _jar;
                req.Timeout = 20000;
                byte[] bytes;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[8192]; int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    bytes = ms.ToArray();
                }
                if (bytes.Length < 100) { error = Encoding.UTF8.GetString(bytes); return null; }
                string zipPath = Path.Combine(Path.GetTempPath(), "tw_keypair_" + DateTime.Now.ToString("HHmmss") + ".zip");
                File.WriteAllBytes(zipPath, bytes);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    List<string> names = new List<string>();
                    foreach (ZipArchiveEntry ent in zip.Entries)
                    {
                        if (ent.Name.Length == 0) continue;
                        ent.ExtractToFile(Path.Combine(dir, ent.Name), true);
                        names.Add(ent.Name);
                    }
                    try { File.Delete(zipPath); } catch { }
                    return names.ToArray();
                }
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        static string EnvelopeError(Dictionary<string, object> d)
        {
            object e;
            if (d != null && d.TryGetValue("error", out e) && e != null && Convert.ToString(e).Length > 0) return Convert.ToString(e);
            return null;
        }

        static bool LooksLikeAuth(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return false;
            string m = msg.ToLowerInvariant();
            return m.Contains("unauthor") || m.Contains("login") || m.Contains("forbidden") || m.Contains("session");
        }

        public static Dictionary<string, object> GetJson(string path, out string error)
        {
            string text = Get(path, out error);
            if (string.IsNullOrEmpty(text)) return null;
            try { return Js.Deserialize<Dictionary<string, object>>(text); }
            catch { return null; }
        }

        // —— 取值辅助 ——
        public static string Str(Dictionary<string, object> d, string k, string def)
        {
            if (d == null) return def;
            object v; if (!d.TryGetValue(k, out v) || v == null) return def;
            return Convert.ToString(v);
        }
        public static int Int(Dictionary<string, object> d, string k, int def)
        {
            if (d == null) return def;
            object v; if (!d.TryGetValue(k, out v) || v == null) return def;
            int n; return int.TryParse(Convert.ToString(v), out n) ? n : def;
        }
        public static double Dbl(Dictionary<string, object> d, string k, double def)
        {
            if (d == null) return def;
            object v; if (!d.TryGetValue(k, out v) || v == null) return def;
            double n;
            if (v is double) return (double)v;
            if (v is float) return (float)v;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            return double.TryParse(Convert.ToString(v), out n) ? n : def;
        }
        public static bool Bool(Dictionary<string, object> d, string k, bool def)
        {
            if (d == null) return def;
            object v; if (!d.TryGetValue(k, out v) || v == null) return def;
            if (v is bool) return (bool)v;
            return Convert.ToString(v) == "1" || string.Equals(Convert.ToString(v), "true", StringComparison.OrdinalIgnoreCase);
        }
        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            object v; if (!d.TryGetValue(k, out v)) return null;
            return v as Dictionary<string, object>;
        }
        public static List<object> Arr(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            object v; if (!d.TryGetValue(k, out v) || v == null) return null;
            List<object> lst = v as List<object>;
            if (lst != null) return lst;
            object[] arr = v as object[];
            if (arr != null) return new List<object>(arr);
            System.Collections.IEnumerable en = v as System.Collections.IEnumerable;
            if (en == null) return null;
            List<object> res = new List<object>();
            foreach (object o in en) res.Add(o);
            return res;
        }
    }

    /// 假人管理（原生面板，直接调 8112 的 /api/call，不再嵌 WebView2）
    public class RobotForm : Form
    {
        static readonly Color Bg = Color.FromArgb(24, 26, 34);
        static readonly Color PanelBg = Color.FromArgb(32, 35, 46);
        static readonly Color PanelBg2 = Color.FromArgb(38, 42, 55);
        static readonly Color Accent = Color.FromArgb(64, 158, 255);
        static readonly Color TextMain = Color.FromArgb(232, 236, 244);
        static readonly Color TextDim = Color.FromArgb(140, 148, 166);
        static readonly Color OkColor = Color.FromArgb(82, 196, 130);
        static readonly Color BadColor = Color.FromArgb(235, 100, 100);
        static readonly Color WarnColor = Color.FromArgb(230, 180, 80);
        static readonly Color Line = Color.FromArgb(48, 52, 66);

        DataGridView _grid;
        Label _lRobot, _lGame, _lOnline, _lConn, _lMode, _lSvc, _lKey, _lAuto, _lStore, _lTotal;
        Label _lblSt;
        System.Windows.Forms.Timer _timer;
        bool _busy;

        public RobotForm()
        {
            Text = "假人管理 · " + Cfg.VmIp + ":8112";
            ClientSize = new Size(1240, 860);
            MinimumSize = new Size(1000, 620);
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // Dock 顺序：Fill 最先加
            BuildGrid();
            BuildStatus();
            BuildToolbar();
            BuildCards();
            BuildHeader();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 5000;
            _timer.Tick += delegate { if (!_busy) RefreshAsync(false); };
            Shown += delegate { _timer.Start(); RefreshAsync(true); };
            FormClosed += delegate { _timer.Stop(); };
        }

        void BuildHeader()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Top;
            p.Height = 68;
            p.BackColor = Bg;
            p.Padding = new Padding(18, 10, 18, 6);

            Label t = new Label();
            t.Text = "假人管理";
            t.AutoSize = true;
            t.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            t.ForeColor = TextMain;
            t.Location = new Point(18, 10);
            p.Controls.Add(t);

            Label s = new Label();
            s.Text = "http://" + Cfg.VmIp + ":8112 · 已自动登录（密码 " + (Cfg.RobotWebPwd ?? "") + "）";
            s.AutoSize = true;
            s.Font = new Font("Microsoft YaHei UI", 8.5F);
            s.ForeColor = TextDim;
            s.Location = new Point(20, 42);
            p.Controls.Add(s);
            Controls.Add(p);
        }

        Panel MakeCard(int x, int y, int w, string key, out Label val)
        {
            Panel c = new Panel();
            c.SetBounds(x, y, w, 76);
            c.BackColor = PanelBg;
            c.Padding = new Padding(12, 8, 12, 8);
            Label k = new Label();
            k.Text = key;
            k.AutoSize = true;
            k.Font = new Font("Microsoft YaHei UI", 8.5F);
            k.ForeColor = TextDim;
            k.Location = new Point(12, 8);
            c.Controls.Add(k);
            Label v = new Label();
            v.Text = "-";
            v.AutoSize = false;
            v.SetBounds(12, 30, w - 24, 34);
            v.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            v.ForeColor = TextMain;
            v.AutoEllipsis = true;
            c.Controls.Add(v);
            val = v;
            return c;
        }

        void BuildCards()
        {
            Panel host = new Panel();
            host.Dock = DockStyle.Top;
            host.Height = 172;
            host.BackColor = Bg;
            host.Padding = new Padding(18, 0, 18, 10);

            int gap = 10;
            Panel row1 = new Panel();
            row1.Dock = DockStyle.Top;
            row1.Height = 76;
            row1.BackColor = Bg;
            Panel row2 = new Panel();
            row2.Dock = DockStyle.Top;
            row2.Height = 76;
            row2.BackColor = Bg;
            row2.Top = 0;

            host.Controls.Add(row2);
            host.Controls.Add(row1);

            row1.Resize += delegate
            {
                int n = 5, w = (row1.ClientSize.Width - gap * (n - 1)) / n;
                for (int i = 0; i < row1.Controls.Count; i++)
                {
                    Panel c = row1.Controls[i] as Panel;
                    if (c != null) c.SetBounds(i * (w + gap), 0, w, 76);
                }
            };
            row2.Resize += delegate
            {
                int n = 5, w = (row2.ClientSize.Width - gap * (n - 1)) / n;
                for (int i = 0; i < row2.Controls.Count; i++)
                {
                    Panel c = row2.Controls[i] as Panel;
                    if (c != null) c.SetBounds(i * (w + gap), 0, w, 76);
                }
            };

            row1.Controls.Add(MakeCard(0, 0, 200, "假人进程", out _lRobot));
            row1.Controls.Add(MakeCard(0, 0, 200, "游戏端口 10011", out _lGame));
            row1.Controls.Add(MakeCard(0, 0, 200, "在线上限（在跑 / 目标）", out _lOnline));
            row1.Controls.Add(MakeCard(0, 0, 200, "上线中", out _lConn));
            row1.Controls.Add(MakeCard(0, 0, 200, "引擎模式", out _lMode));

            row2.Controls.Add(MakeCard(0, 0, 200, "服务 M / A / G / R", out _lSvc));
            row2.Controls.Add(MakeCard(0, 0, 200, "密钥", out _lKey));
            row2.Controls.Add(MakeCard(0, 0, 200, "自动行为", out _lAuto));
            row2.Controls.Add(MakeCard(0, 0, 200, "摆摊（人 / 挂摊 / 分离）", out _lStore));
            row2.Controls.Add(MakeCard(0, 0, 200, "假人总数", out _lTotal));

            Controls.Add(host);
        }

        RoundButton TbBtn(string text, bool accent, EventHandler onClick)
        {
            RoundButton b = new RoundButton();
            b.Text = text;
            b.Width = 74;
            b.Height = 32;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = accent ? 0 : 1;
            b.FlatAppearance.BorderColor = Line;
            b.BackColor = accent ? Accent : PanelBg;
            b.ForeColor = accent ? Color.White : TextMain;
            b.Font = new Font("Microsoft YaHei UI", 9F, accent ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            b.Click += onClick;
            return b;
        }

        void BuildToolbar()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Top;
            p.Height = 46;
            p.BackColor = Bg;
            p.Padding = new Padding(18, 6, 18, 6);

            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Dock = DockStyle.Fill;
            f.FlowDirection = FlowDirection.LeftToRight;
            f.WrapContents = false;
            f.BackColor = Bg;
            f.Padding = new Padding(0);

            f.Controls.Add(TbBtn("刷新", true, delegate { RefreshAsync(true); }));
            f.Controls.Add(TbBtn("自动 开/关", false, delegate { ToggleAuto(); }));
            f.Controls.Add(TbBtn("上线", false, delegate { RunScoped("上线假人", "robotsOnlineAsync", 10); }));
            f.Controls.Add(TbBtn("移动", false, delegate { RunScoped("移动假人", "robotsMove", 10); }));
            f.Controls.Add(TbBtn("喊话", false, delegate { RunScoped("假人喊话", "robotsShout", 10); }));
            f.Controls.Add(TbBtn("摆摊", false, delegate { RunScoped("摆摊", "robotsStoreAsync", 10); }));
            f.Controls.Add(TbBtn("下线", false, delegate { RunScoped("下线假人", "robotsLogoutAsync", 10); }));
            f.Controls.Add(TbBtn("清理", false, delegate { Cleanup(); }));
            f.Controls.Add(TbBtn("参数", false, delegate { OpenConfig(); }));
            f.Controls.Add(TbBtn("市场", false, delegate { OpenMarket(); }));
            f.Controls.Add(TbBtn("重启拍卖", false, delegate { RestartAuction(); }));
            f.Controls.Add(TbBtn("密钥", false, delegate { ShowKey(); }));
            f.Controls.Add(TbBtn("诊断", false, delegate { OpenBrowser(); }));
            f.Controls.Add(TbBtn("网页版", false, delegate { OpenBrowser(); }));

            p.Controls.Add(f);
            Controls.Add(p);
        }

        void BuildGrid()
        {
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.BackgroundColor = Bg;
            _grid.GridColor = Line;
            _grid.BorderStyle = BorderStyle.None;
            _grid.RowHeadersVisible = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.ReadOnly = true;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersHeight = 30;
            _grid.RowTemplate.Height = 24;
            _grid.Font = new Font("Microsoft YaHei UI", 9F);

            _grid.ColumnHeadersDefaultCellStyle.BackColor = PanelBg;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDim;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = PanelBg;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F);

            _grid.DefaultCellStyle.BackColor = Bg;
            _grid.DefaultCellStyle.ForeColor = TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(44, 78, 120);
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;

            _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(28, 30, 39);
            _grid.AlternatingRowsDefaultCellStyle.ForeColor = TextMain;

            string[] cols = { "UID", "CID", "名字", "Actor", "状态", "等级", "职业", "城镇", "区域", "X", "Y", "在线", "摆摊" };
            int[] widths = { 78, 58, 120, 100, 150, 48, 96, 110, 48, 48, 48, 70, 66 };
            for (int i = 0; i < cols.Length; i++)
            {
                DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
                c.HeaderText = cols[i];
                c.Width = widths[i];
                c.SortMode = DataGridViewColumnSortMode.NotSortable;
                _grid.Columns.Add(c);
            }

            Panel wrap = new Panel();
            wrap.Dock = DockStyle.Fill;
            wrap.BackColor = Bg;
            wrap.Padding = new Padding(18, 4, 18, 6);
            wrap.Controls.Add(_grid);
            Controls.Add(wrap);
        }

        void BuildStatus()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Bottom;
            p.Height = 32;
            p.BackColor = PanelBg;

            _lblSt = new Label();
            _lblSt.Text = "就绪";
            _lblSt.AutoSize = false;
            _lblSt.Dock = DockStyle.Fill;
            _lblSt.Padding = new Padding(14, 0, 0, 0);
            _lblSt.TextAlign = ContentAlignment.MiddleLeft;
            _lblSt.ForeColor = TextDim;
            _lblSt.Font = new Font("Microsoft YaHei UI", 9F);
            p.Controls.Add(_lblSt);
            Controls.Add(p);
        }

        /// 重启拍卖行/金币寄售：服务启动时才建搜索索引，假人补货是直接写库，
        /// 不重启就搜不到新挂单，所以这里给一个手动按钮（另有 30 分钟定时任务兜底）。
        void RestartAuction()
        {
            if (_busy) return;
            if (MessageBox.Show("重启拍卖行（30803）和金币寄售（30603）？\n\n大约 10 秒，期间玩家打不开拍卖行；\n重启后服务会重新读取 auction_main，新补的货才能搜到。",
                    "重启拍卖行", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            _busy = true;
            SetSt("正在重启拍卖行 ……", Accent);
            Task.Run(delegate
            {
                string outp;
                int code = RobotApi.RunScript("dnf_auction_ctl.py", "restart", out outp);
                BeginInvoke(new Action(delegate
                {
                    _busy = false;
                    string tip = (outp ?? "").Trim();
                    if (tip.Length > 900) tip = tip.Substring(tip.Length - 900);
                    if (string.IsNullOrEmpty(tip)) tip = "（脚本没有输出）";
                    SetSt(code == 0 ? "拍卖行已重启，索引已重建" : "重启失败，看弹窗内容", code == 0 ? OkColor : BadColor);
                    MessageBox.Show(tip, code == 0 ? "重启完成" : "重启失败", MessageBoxButtons.OK,
                        code == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }));
            });
        }

        void SetSt(string text, Color c)
        {
            if (InvokeRequired) { BeginInvoke(new Action(delegate { SetSt(text, c); })); return; }
            _lblSt.Text = text;
            _lblSt.ForeColor = c;
        }

        string JobName(int job)
        {
            string[] n = { "鬼剑士", "格斗家", "神枪手", "魔法师", "圣职者", "暗夜使者", "守护者", "魔枪士", "枪剑士", "外传", "缔造者" };
            return job >= 0 && job < n.Length ? n[job] : ("职业" + job);
        }

        void RefreshAsync(bool verbose)
        {
            if (_busy) return;
            _busy = true;
            SetSt("正在读取假人状态 ……", TextDim);
            Task.Run(new Action(delegate
            {
                string e1, e2, e3, e4, e5, e6, e7, e8, e9;
                Dictionary<string, object> auto = RobotApi.Call("autoStatus", null, out e1, 20000);
                Dictionary<string, object> sys = RobotApi.Call("systemStatus", null, out e2, 20000);
                Dictionary<string, object> sch = RobotApi.Call("schedulerStatus", null, out e3, 20000);
                Dictionary<string, object> key = RobotApi.Call("keypairStatus", null, out e4, 20000);
                Dictionary<string, object> robots = RobotApi.Call("robotsStatus",
                    new Dictionary<string, object> { { "count", 1000 } }, out e5, 90000);
                Dictionary<string, object> mkt = RobotApi.Call("marketStatus", null, out e6, 30000);
                Dictionary<string, object> mon = RobotApi.GetJson("/api/monitor-service", out e7);
                Dictionary<string, object> relay = RobotApi.GetJson("/api/relay-service", out e8);
                Dictionary<string, object> gp = RobotApi.GetJson("/api/game-port", out e9);

                BeginInvoke(new Action(delegate
                {
                    _busy = false;
                    if (auto == null && robots == null)
                    {
                        SetSt("读取失败：" + (string.IsNullOrEmpty(e1) ? "连不上假人管理页" : e1) + "（服务端没开机，或假人没跑起来）", BadColor);
                        _lRobot.Text = "未连接";
                        _lRobot.ForeColor = BadColor;
                        return;
                    }
                    UpdateCards(auto, sys, sch, key, robots, mkt, mon, relay, gp);
                    string fillErr = null;
                    if (robots != null)
                    {
                        try { FillGrid(robots); }
                        catch (Exception fex) { fillErr = fex.GetType().Name + ": " + fex.Message; }
                    }
                    else fillErr = "robotsStatus 读取失败：" + e5;
                    string tip = "已刷新 " + DateTime.Now.ToString("HH:mm:ss") + (fillErr == null ? "" : "   [列表] " + fillErr);
                    if (verbose && !string.IsNullOrEmpty(e6)) tip += "   市场：" + e6;
                    SetSt(tip, TextDim);
                }));
            }));
        }

        static string Ok(bool b) { return b ? "✓" : "✗"; }

        void UpdateCards(Dictionary<string, object> auto, Dictionary<string, object> sys, Dictionary<string, object> sch,
                         Dictionary<string, object> key, Dictionary<string, object> robots, Dictionary<string, object> mkt,
                         Dictionary<string, object> mon, Dictionary<string, object> relay, Dictionary<string, object> gp)
        {
            double cpu = 0;
            object v;
            if (sys != null && sys.TryGetValue("robot_cpu_percent", out v) && v != null) double.TryParse(Convert.ToString(v), out cpu);
            int mem = RobotApi.Int(sys, "robot_memory_mb", 0);
            int thr = RobotApi.Int(sys, "robot_threads", 0);
            int up = RobotApi.Int(sys, "robot_uptime_seconds", 0);
            if (sys != null)
            {
                _lRobot.Text = cpu.ToString("0.0") + "%   " + mem + "MB";
                _lRobot.ForeColor = OkColor;
                _lRobot.Tag = up;
            }
            else { _lRobot.Text = "-"; _lRobot.ForeColor = TextDim; }

            bool portReady = RobotApi.Bool(auto, "game_port_ready", false);
            string addr = RobotApi.Str(auto, "game_port_address", RobotApi.Str(gp, "addr", ""));
            int maxUser = RobotApi.Int(gp, "max_user_num", 0);
            _lGame.Text = portReady ? ("Open" + (maxUser > 0 ? " · 上限" + maxUser : "")) : "Closed";
            _lGame.ForeColor = portReady ? OkColor : BadColor;
            if (!string.IsNullOrEmpty(addr)) _lGame.Tag = addr;

            int running = RobotApi.Int(auto, "running", RobotApi.Int(sys, "running", 0));
            int target = RobotApi.Int(auto, "target_online", 0);
            _lOnline.Text = running + " / " + target;
            _lOnline.ForeColor = (target > 0 && running >= target) ? OkColor : TextMain;

            int conn = RobotApi.Int(auto, "connecting", 0);
            _lConn.Text = conn.ToString();
            _lConn.ForeColor = conn > 0 ? WarnColor : TextDim;

            string mode = RobotApi.Str(sch, "mode", "-");
            bool breaker = RobotApi.Bool(sch, "breaker_active", false);
            _lMode.Text = breaker ? (mode + " · 熔断") : mode;
            _lMode.ForeColor = breaker ? BadColor : TextMain;

            Dictionary<string, object> svc = RobotApi.Obj(mkt, "services");
            bool aucOk = RobotApi.Str(RobotApi.Obj(svc, "auction"), "status", "") == "ready";
            bool ptOk = RobotApi.Str(RobotApi.Obj(svc, "point"), "status", "") == "ready";
            bool monOk = RobotApi.Str(mon, "state", "") == "open";
            bool relOk = RobotApi.Str(relay, "state", "") == "open";
            _lSvc.Text = "M" + Ok(monOk) + "  A" + Ok(aucOk) + "  G" + Ok(ptOk) + "  R" + Ok(relOk);
            _lSvc.ForeColor = (monOk && relOk && aucOk && ptOk) ? OkColor : WarnColor;

            string ks = RobotApi.Str(key, "key_state", "-");
            bool gv = RobotApi.Bool(key, "game_valid", false);
            bool cv = RobotApi.Bool(key, "config_valid", false);
            _lKey.Text = gv && cv ? ("已应用 · " + ks) : (ks + " · 未同步");
            _lKey.ForeColor = gv && cv ? OkColor : BadColor;

            bool autoOn = RobotApi.Bool(auto, "enabled", false);
            _lAuto.Text = autoOn ? "On" : "Off";
            _lAuto.ForeColor = autoOn ? OkColor : WarnColor;

            int stR = RobotApi.Int(auto, "store_running", 0);
            int stI = RobotApi.Int(auto, "store_item_running", 0);
            int stD = RobotApi.Int(auto, "store_disjoint_running", 0);
            _lStore.Text = stR + " (" + stI + " / " + stD + ")";
            _lStore.ForeColor = TextMain;

            int total = RobotApi.Int(robots, "total", 0);
            int rn = RobotApi.Int(robots, "running", 0);
            _lTotal.Text = total + " 个（在线 " + rn + "）";
            _lTotal.ForeColor = TextMain;
        }

        void FillGrid(Dictionary<string, object> robots)
        {
            List<object> rows = RobotApi.Arr(robots, "robots");
            _grid.SuspendLayout();
            _grid.Rows.Clear();
            if (rows != null)
            {
                foreach (object o in rows)
                {
                    Dictionary<string, object> r = o as Dictionary<string, object>;
                    if (r == null) continue;
                    Dictionary<string, object> rs = RobotApi.Obj(r, "robot_state");
                    string act = RobotApi.Bool(r, "actor_attached", false)
                        ? ("slot " + RobotApi.Int(r, "actor_slot", 0) + " " + RobotApi.Str(r, "actor_state", ""))
                        : "free";
                    if (RobotApi.Str(r, "operation", "") == "cleanup") act = "删除中";
                    string state = RobotApi.Str(rs, "actual_state", "-") + " → " + RobotApi.Str(rs, "desired_state", "-");
                    if (RobotApi.Str(rs, "last_error", "").Length > 0) state += " !";
                    string store = "";
                    if (RobotApi.Int(r, "robot_type", 0) == 3 && RobotApi.Bool(r, "disjoint_active", false)) store = "分离摊";
                    else if (RobotApi.Bool(r, "store_display_ack", false)) store = "挂摊中";
                    else if (RobotApi.Bool(r, "store_created", false)) store = "准备中";
                    int ups = RobotApi.Int(r, "uptime_seconds", 0);
                    _grid.Rows.Add(
                        RobotApi.Int(r, "uid", 0),
                        RobotApi.Int(r, "cid", 0),
                        RobotApi.Str(r, "name", ""),
                        act,
                        state,
                        RobotApi.Int(r, "level", 0),
                        JobName(RobotApi.Int(r, "job", 0)),
                        RobotApi.Str(r, "village_name", "") != "" ? RobotApi.Str(r, "village_name", "") : ("村" + RobotApi.Int(r, "village", 0)),
                        RobotApi.Int(r, "area", 0),
                        RobotApi.Int(r, "x", 0),
                        RobotApi.Int(r, "y", 0),
                        (ups / 60) + "m",
                        store);
                    DataGridViewRow row = _grid.Rows[_grid.Rows.Count - 1];
                    int idx = _grid.Rows.Count - 1;
                    if (state.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        state.IndexOf("disconnected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        state.EndsWith("!"))
                        row.DefaultCellStyle.ForeColor = BadColor;
                    else if (!RobotApi.Bool(r, "online", false))
                        row.DefaultCellStyle.ForeColor = TextDim;
                }
            }
            _grid.ResumeLayout();
        }

        List<int> SelectedUids()
        {
            List<int> list = new List<int>();
            foreach (DataGridViewRow row in _grid.SelectedRows)
            {
                object v = row.Cells[0].Value;
                int uid;
                if (v != null && int.TryParse(Convert.ToString(v), out uid)) list.Add(uid);
            }
            return list;
        }

        void ToggleAuto()
        {
            string err;
            Dictionary<string, object> a = RobotApi.Call("autoStatus", null, out err, 10000);
            bool on = RobotApi.Bool(a, "enabled", false);
            string cmd = on ? "autoStop" : "autoStart";
            SetSt((on ? "正在关闭自动行为……" : "正在开启自动行为……"), TextDim);
            Task.Run(new Action(delegate
            {
                string e;
                Dictionary<string, object> r = RobotApi.Call(cmd, null, out e, 30000);
                BeginInvoke(new Action(delegate
                {
                    if (r == null) SetSt("操作失败：" + e, BadColor);
                    else { SetSt(on ? "自动行为已关闭（auto_actions=false）" : "自动行为已开启（auto_actions=true）", OkColor); RefreshAsync(false); }
                }));
            }));
        }

        void RunScoped(string title, string command, int defCount)
        {
            List<int> uids = SelectedUids();
            string payload;
            if (uids.Count > 0)
            {
                payload = "{\"uids\":[" + string.Join(",", uids.ConvertAll(delegate (int i) { return i.ToString(); }).ToArray()) + "]}";
                SetSt(title + "：" + uids.Count + " 个（已选）……", TextDim);
            }
            else
            {
                string input = InputBox.Show(title, "数量（作用于所有空闲假人，范围 1~" + Math.Max(1, _grid.Rows.Count) + "）", defCount.ToString());
                if (input == null) return;
                int n;
                if (!int.TryParse(input, out n) || n <= 0) { SetSt("数量不对", BadColor); return; }
                payload = "{\"count\":" + n + "}";
                SetSt(title + "：" + n + " 个 ……", TextDim);
            }
            Task.Run(new Action(delegate
            {
                string e;
                Dictionary<string, object> body = RobotApi.Js.Deserialize<Dictionary<string, object>>(payload);
                Dictionary<string, object> r = RobotApi.Call(command, body, out e, 120000);
                BeginInvoke(new Action(delegate
                {
                    if (r == null) SetSt(title + "失败：" + e, BadColor);
                    else
                    {
                        Dictionary<string, object> res = RobotApi.Obj(r, "result");
                        string sum = res == null ? "" :
                            ("请求 " + RobotApi.Int(res, "requested", 0) + " / 受理 " + RobotApi.Int(res, "accepted", 0) +
                             " / 确认 " + RobotApi.Int(res, "confirmed", 0) + " / 失败 " + RobotApi.Int(res, "failed", 0));
                        SetSt(title + " 已提交 " + sum, OkColor);
                        RefreshAsync(false);
                    }
                }));
            }));
        }

        void Cleanup()
        {
            List<int> uids = SelectedUids();
            string msg = uids.Count > 0
                ? ("确定删除选中的 " + uids.Count + " 个假人吗？（连带角色数据）")
                : "确定清理全部假人吗？（只删机器人账号，真人 UID 有保护）";
            if (MessageBox.Show(msg, "清理假人", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            Task.Run(new Action(delegate
            {
                string e;
                Dictionary<string, object> body = new Dictionary<string, object>();
                if (uids.Count > 0) body["uids"] = uids;
                body["force"] = true;
                Dictionary<string, object> r = RobotApi.Call("cleanupRobotsAsync", body, out e, 60000);
                BeginInvoke(new Action(delegate
                {
                    if (r == null) SetSt("清理失败：" + e, BadColor);
                    else { SetSt("清理任务已提交（后台执行），稍后刷新看结果", OkColor); RefreshAsync(false); }
                }));
            }));
        }

        void OpenConfig()
        {
            using (RobotConfigForm f = new RobotConfigForm())
            {
                f.ShowDialog(this);
            }
        }

        void OpenMarket()
        {
            using (MarketForm f = new MarketForm())
            {
                f.ShowDialog(this);
            }
        }

        void OpenBrowser()
        {
            try { Process.Start(RobotApi.Base + "/"); } catch (Exception ex) { MessageBox.Show(ex.Message, "打不开"); }
        }

        void ShowKey()
        {
            string err;
            Dictionary<string, object> r = RobotApi.Call("keypairStatus", null, out err, 15000);
            if (r == null) { SetSt("读取密钥状态失败：" + err, BadColor); return; }
            string text =
                "状态：" + RobotApi.Str(r, "key_state", "-") + "\n" +
                "游戏侧有效：" + (RobotApi.Bool(r, "game_valid", false) ? "是" : "否") + "\n" +
                "配置侧有效：" + (RobotApi.Bool(r, "config_valid", false) ? "是" : "否") + "\n" +
                "内置默认可用：" + (RobotApi.Bool(r, "default_valid", false) ? "是" : "否") + "\n" +
                "指纹：" + RobotApi.Str(r, "fingerprint", "-") + "\n" +
                "游戏私钥：" + RobotApi.Str(r, "game_private", "-") + "\n" +
                "游戏公钥：" + RobotApi.Str(r, "game_public", "-") + "\n\n" +
                "要重新应用内置默认密钥，点【是】。";
            DialogResult dr = MessageBox.Show(text + "\n\n【是】= 应用内置默认密钥（并同步到游戏服务端）\n【否】= 把密钥下载到本机客户端目录",
                "假人密钥", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
            if (dr == DialogResult.Cancel) return;
            if (dr == DialogResult.No)
            {
                Task.Run(new Action(delegate
                {
                    string e;
                    string[] files = RobotApi.DownloadKeypair(Cfg.ClientDir, out e);
                    BeginInvoke(new Action(delegate
                    {
                        if (files == null) SetSt("下载密钥失败：" + e, BadColor);
                        else SetSt("已下载到客户端目录：" + string.Join(" / ", files), OkColor);
                    }));
                }));
                return;
            }
            Task.Run(new Action(delegate
            {
                string e;
                Dictionary<string, object> rr = RobotApi.Call("keypairReleaseDefault", null, out e, 60000);
                BeginInvoke(new Action(delegate
                {
                    if (rr == null) SetSt("应用默认密钥失败：" + e, BadColor);
                    else { SetSt("已应用内置默认密钥", OkColor); RefreshAsync(false); }
                }));
            }));
        }
    }

    /// 假人参数（把 /root/config/conf/robot_config.ini 做成可视化编辑）
    public class RobotConfigForm : Form
    {
        static readonly Color Bg = Color.FromArgb(24, 26, 34);
        static readonly Color PanelBg = Color.FromArgb(32, 35, 46);
        static readonly Color PanelBg2 = Color.FromArgb(38, 42, 55);
        static readonly Color Accent = Color.FromArgb(64, 158, 255);
        static readonly Color TextMain = Color.FromArgb(232, 236, 244);
        static readonly Color TextDim = Color.FromArgb(140, 148, 166);
        static readonly Color OkColor = Color.FromArgb(82, 196, 130);
        static readonly Color BadColor = Color.FromArgb(235, 100, 100);
        static readonly Color Line = Color.FromArgb(48, 52, 66);

        class Entry
        {
            public string Key;
            public string Value;
            public string Hint;
            public int Line;
            public TextBox Box;
            public CheckBox Chk;
        }

        class Sect
        {
            public string Name;
            public string Title;
            public List<Entry> Items = new List<Entry>();
            public Panel Page;
        }

        List<string> _lines = new List<string>();
        List<Sect> _sects = new List<Sect>();
        List<Button> _navBtns = new List<Button>();
        string _tmp;
        Label _lblStatus;
        Panel _content;
        Panel _navBody;
        int _current = 0;
        bool _loaded;
        Panel _nav;

        public RobotConfigForm()
        {
            Text = "假人参数";
            ClientSize = new Size(1010, 730);
            MinimumSize = new Size(900, 640);
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Microsoft YaHei UI", 9.5F);
            StartPosition = FormStartPosition.CenterParent;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _tmp = Path.Combine(Path.GetTempPath(), "dnf_robot_config_" + DateTime.Now.ToString("HHmmss") + ".ini");

            // ── 顶部标题 ──
            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 74;
            head.BackColor = PanelBg;
            head.Paint += delegate(object o, PaintEventArgs e)
            {
                using (Pen p = new Pen(Line)) e.Graphics.DrawLine(p, 0, head.Height - 1, head.Width, head.Height - 1);
            };
            Controls.Add(head);

            Label t = new Label();
            t.Text = "假人参数";
            t.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            t.ForeColor = TextMain;
            t.AutoSize = true;
            t.Location = new Point(20, 12);
            head.Controls.Add(t);

            Label sub = new Label();
            sub.Text = "服务端 /root/config/conf/robot_config.ini · 改完点【保存并重启假人】生效";
            sub.ForeColor = TextDim;
            sub.AutoSize = true;
            sub.Location = new Point(22, 46);
            head.Controls.Add(sub);

            // ── 底部按钮 ──
            Panel foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = 66;
            foot.BackColor = PanelBg;
            foot.Paint += delegate(object o, PaintEventArgs e)
            {
                using (Pen p = new Pen(Line)) e.Graphics.DrawLine(p, 0, 0, foot.Width, 0);
            };
            Controls.Add(foot);

            Button btnSave = MkBtn("保存并重启假人", 20, 14, 168, true);
            btnSave.Click += delegate { Save(true); };
            foot.Controls.Add(btnSave);

            Button btnSaveOnly = MkBtn("仅保存", 200, 14, 96, false);
            btnSaveOnly.Click += delegate { Save(false); };
            foot.Controls.Add(btnSaveOnly);

            Button btnReload = MkBtn("重新读取", 308, 14, 96, false);
            btnReload.Click += delegate { LoadFromServer(); };
            foot.Controls.Add(btnReload);

            Button btnCancel = MkBtn("关闭", 416, 14, 80, false);
            btnCancel.Click += delegate { Close(); };
            foot.Controls.Add(btnCancel);

            _lblStatus = new Label();
            _lblStatus.Text = "正在读取服务端配置 ……";
            _lblStatus.ForeColor = TextDim;
            _lblStatus.AutoSize = true;
            _lblStatus.Location = new Point(516, 23);
            foot.Controls.Add(_lblStatus);

            // ── 左侧导航 + 右侧内容：用绝对定位（Resize 时重算），彻底避开 Dock 冲突 ──
            _nav = new Panel();
            _nav.BackColor = Bg;
            _nav.Padding = new Padding(12, 12, 8, 12);
            Controls.Add(_nav);

            _navBody = new Panel();
            _navBody.Dock = DockStyle.Fill;
            _navBody.BackColor = Bg;
            _navBody.AutoScroll = true;
            _nav.Controls.Add(_navBody);

            _content = new Panel();
            _content.BackColor = Bg;
            _content.AutoScroll = false;
            _content.Padding = new Padding(18, 14, 18, 14);
            Controls.Add(_content);

            SetDoubleBuffered(this);
            SetDoubleBuffered(_content);
            SetDoubleBuffered(_nav);
            SetDoubleBuffered(_navBody);
            Resize += delegate { LayoutBody(); };
            Shown += delegate { LayoutBody(); LoadFromServer(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;   // WS_EX_COMPOSITED：整窗双缓冲，切组不再闪白
                return cp;
            }
        }

        static void SetDoubleBuffered(Control c)
        {
            if (c == null) return;
            try
            {
                System.Reflection.PropertyInfo pi = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (pi != null) pi.SetValue(c, true, null);
            }
            catch { }
        }

        void LayoutBody()
        {
            int top = 74, footH = 66, navW = 186;
            int h = ClientSize.Height - top - footH;
            if (h < 120) h = 120;
            _nav.SetBounds(0, top, navW, h);
            _content.SetBounds(navW, top, Math.Max(200, ClientSize.Width - navW), h);
        }

        Button MkBtn(string text, int x, int y, int w, bool accent)
        {
            Button b = new RoundButton();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, 38);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = accent ? Accent : Color.FromArgb(58, 62, 78);
            b.BackColor = accent ? Accent : PanelBg2;
            b.ForeColor = accent ? Color.White : TextMain;
            b.Font = new Font("Microsoft YaHei UI", 9.5F, accent ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            return b;
        }

        /// 跑一次 scripts 里的脚本，返回 (退出码, 输出)
        int RunPy(string args, out string output)
        {
            return RunPyScript("dnf_robot_config.py", args, out output);
        }

        /// 跑一次指定脚本，返回 (退出码, 输出)
        int RunPyScript(string scriptName, string args, out string output)
        {
            return RobotApi.RunScript(scriptName, args, out output);
        }
        int RunPyScriptOld(string scriptName, string args, out string output)
        {
            string script = Cfg.Script(scriptName);
            output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Cfg.Python;
                psi.Arguments = "\"" + script + "\" " + args;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.GetEncoding(936);
                psi.StandardErrorEncoding = Encoding.GetEncoding(936);
                psi.WorkingDirectory = Cfg.ToolsDir;
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "gbk";
                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                psi.EnvironmentVariables["DNF_SSH_HOST"] = Cfg.VmIp;
                psi.EnvironmentVariables["DNF_SSH_PORT"] = Cfg.SshPort.ToString();
                psi.EnvironmentVariables["DNF_SSH_USER"] = Cfg.SshUser;
                psi.EnvironmentVariables["DNF_SSH_PWD"] = Cfg.SshPwd;
                using (Process p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(240000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }

        void LoadFromServer()
        {
            _lblStatus.ForeColor = TextDim;
            _lblStatus.Text = "正在读取服务端配置 ……";
            Cursor = Cursors.WaitCursor;
            string err;
            Dictionary<string, object> res = RobotApi.Call("robotConfigGet", null, out err, 30000);
            Cursor = Cursors.Default;
            string text = res == null ? "" : RobotApi.Str(res, "text", "");
            if (string.IsNullOrEmpty(text))
            {
                _lblStatus.ForeColor = BadColor;
                _lblStatus.Text = "读取失败（服务端没开机？）";
                MessageBox.Show("读取假人参数失败：\n\n" + (string.IsNullOrEmpty(err) ? "返回为空" : err.Trim()) +
                    "\n\n请先【一键启动联机】把服务端跑起来。",
                    "假人参数", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            File.WriteAllText(_tmp, text, new UTF8Encoding(false));
            _lines.Clear();
            string[] arr = text.Replace("\r\n", "\n").Split('\n');
            int last = arr.Length;
            while (last > 0 && arr[last - 1].Trim().Length == 0) last--;
            for (int i = 0; i < last; i++) _lines.Add(arr[i]);
            Parse();
            BuildUi();
            _loaded = true;
            _lblStatus.ForeColor = OkColor;
            _lblStatus.Text = "已读取 " + _sects.Count + " 组 / " + CountEntries() + " 项参数";
        }

        int CountEntries()
        {
            int n = 0;
            foreach (Sect s in _sects) n += s.Items.Count;
            return n;
        }

        void Parse()
        {
            _sects.Clear();
            Sect cur = null;
            string pendingHint = "";
            for (int i = 0; i < _lines.Count; i++)
            {
                string raw = _lines[i];
                string line = raw.Trim();
                if (line.Length == 0) { continue; }
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    cur = new Sect();
                    cur.Name = line.Substring(1, line.Length - 2).Trim();
                    cur.Title = SectionTitle(cur.Name);
                    _sects.Add(cur);
                    pendingHint = "";
                    continue;
                }
                if (line.StartsWith("#") || line.StartsWith(";"))
                {
                    string h = line.TrimStart('#', ';', ' ');
                    if (h.Length > 0) pendingHint = h;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0 || cur == null) { pendingHint = ""; continue; }
                Entry e = new Entry();
                e.Key = line.Substring(0, eq).Trim();
                e.Value = line.Substring(eq + 1).Trim();
                e.Hint = pendingHint;
                e.Line = i;
                cur.Items.Add(e);
                pendingHint = "";
            }
            List<Sect> keep = new List<Sect>();
            foreach (Sect sec in _sects) if (sec.Items.Count > 0) keep.Add(sec);
            _sects = keep;
        }

        string SectionTitle(string name)
        {
            switch (name)
            {
                case "create": return "创建 / 等级职业";
                case "spawn": return "出生 / 城镇坐标";
                case "equipment": return "装备生成";
                case "avatar": return "时装生成";
                case "pet": return "宠物";
                case "store": return "摆摊卖货";
                case "follow": return "跟随移动";
                case "shout": return "喊话";
                case "auto": return "自动调度";
                case "scheduler": return "调度器";
                default: return name;
            }
        }

        void BuildUi()
        {
            SuspendLayout();
            _navBody.Controls.Clear();
            _content.Controls.Clear();
            _navBtns.Clear();
            _current = -1;   // 关键：清空后必须重置，否则 SelectSection(0) 会早退，内容区空白

            int y = 0;
            for (int i = 0; i < _sects.Count; i++)
            {
                Sect sec = _sects[i];
                sec.Page = BuildPage(sec);

                Button nb = new RoundButton();
                nb.Text = sec.Title;
                nb.TextAlign = ContentAlignment.MiddleLeft;
                nb.Padding = new Padding(12, 0, 0, 0);
                nb.Location = new Point(0, y);
                nb.Size = new Size(158, 40);
                nb.FlatStyle = FlatStyle.Flat;
                nb.FlatAppearance.BorderSize = 1;
                nb.FlatAppearance.BorderColor = PanelBg;
                nb.BackColor = PanelBg;
                nb.ForeColor = TextMain;
                nb.Font = new Font("Microsoft YaHei UI", 9.5F);
                nb.Cursor = Cursors.Hand;
                int idx = i;
                nb.Click += delegate { SelectSection(idx); };
                nb.MouseEnter += delegate
                {
                    if (_current != idx) nb.BackColor = PanelBg2;
                };
                nb.MouseLeave += delegate
                {
                    if (_current != idx) nb.BackColor = PanelBg;
                };
                _navBody.Controls.Add(nb);
                _navBtns.Add(nb);
                y += 44;
            }
            SelectSection(0);
            ResumeLayout();
        }

        Panel BuildPage(Sect sec)
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Bg;
            page.AutoScroll = true;
            page.Visible = false;   // 等加进容器、布局好再显示，避免闪白

            Label t = new Label();
            t.Text = sec.Title;
            t.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            t.ForeColor = Accent;
            t.AutoSize = true;
            t.Location = new Point(4, 2);
            page.Controls.Add(t);

            Label c = new Label();
            c.Text = sec.Items.Count + " 项";
            c.ForeColor = TextDim;
            c.AutoSize = true;
            c.Location = new Point(4 + t.PreferredWidth + 12, 8);
            page.Controls.Add(c);

            Panel line = new Panel();
            line.Location = new Point(2, 34);
            line.Size = new Size(680, 1);
            line.BackColor = Line;
            page.Controls.Add(line);

            int y = 46;
            foreach (Entry e in sec.Items)
            {
                Panel row = new Panel();
                row.Location = new Point(2, y);
                row.Size = new Size(700, e.Hint.Length > 0 ? 54 : 40);
                row.BackColor = Bg;
                page.Controls.Add(row);

                Label k = new Label();
                k.Text = e.Key;
                k.Font = new Font("Consolas", 10F, FontStyle.Bold);
                k.ForeColor = TextMain;
                k.AutoSize = true;
                k.Location = new Point(10, 6);
                row.Controls.Add(k);

                if (e.Value == "true" || e.Value == "false")
                {
                    CheckBox chk = new CheckBox();
                    chk.Text = "开启";
                    chk.ForeColor = TextDim;
                    chk.AutoSize = true;
                    chk.Location = new Point(316, 5);
                    chk.Checked = (e.Value == "true");
                    row.Controls.Add(chk);
                    e.Chk = chk;
                }
                else
                {
                    TextBox box = new TextBox();
                    box.Text = e.Value;
                    box.Location = new Point(316, 4);
                    box.Width = 356;
                    box.BackColor = PanelBg2;
                    box.ForeColor = TextMain;
                    box.BorderStyle = BorderStyle.FixedSingle;
                    row.Controls.Add(box);
                    e.Box = box;
                }

                if (e.Hint.Length > 0)
                {
                    Label h = new Label();
                    h.Text = e.Hint;
                    h.ForeColor = TextDim;
                    h.Font = new Font("Microsoft YaHei UI", 8.5F);
                    h.AutoSize = false;
                    h.AutoEllipsis = true;
                    h.Location = new Point(10, 30);
                    h.Size = new Size(676, 17);
                    row.Controls.Add(h);
                }
                y += row.Height + 6;
            }
            return page;
        }

        void Harvest()
        {
            foreach (Sect st in _sects)
            {
                if (st.Page == null) continue;
                foreach (Entry e in st.Items)
                {
                    if (e.Box != null && !e.Box.IsDisposed) e.Value = e.Box.Text;
                    else if (e.Chk != null && !e.Chk.IsDisposed) e.Value = e.Chk.Checked ? "true" : "false";
                }
            }
        }

        void SelectSection(int idx)
        {
            if (idx < 0 || idx >= _sects.Count) return;
            if (idx == _current && _sects[idx].Page != null) return;
            Harvest();
            _current = idx;
            _content.SuspendLayout();

            Sect cur = _sects[idx];
            Panel oldPage = cur.Page;
            Panel fresh = BuildPage(cur);
            cur.Page = fresh;
            _content.Controls.Add(fresh);
            _content.ResumeLayout(false);
            _content.PerformLayout();

            for (int i = 0; i < _sects.Count; i++)
                if (i != idx) _sects[i].Page = null;

            fresh.Visible = true;
            fresh.BringToFront();
            if (oldPage != null && !oldPage.IsDisposed)
            {
                _content.Controls.Remove(oldPage);
                oldPage.Dispose();
            }
            for (int i = 0; i < _navBtns.Count; i++)
            {
                bool on = (i == idx);
                _navBtns[i].BackColor = on ? Accent : PanelBg;
                _navBtns[i].ForeColor = on ? Color.White : TextMain;
                _navBtns[i].Font = new Font("Microsoft YaHei UI", 9.5F, on ? FontStyle.Bold : FontStyle.Regular);
                _navBtns[i].FlatAppearance.BorderColor = on ? Accent : PanelBg;
            }
        }

        void Save(bool restart)
        {
            if (!_loaded)
            {
                MessageBox.Show("还没读到配置，先点【重新读取】。", "假人参数", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Harvest();   // 先把当前页面上改过的值收回 Entry，再整体写回
            foreach (Sect s in _sects)
            {
                foreach (Entry e in s.Items)
                {
                    string v = (e.Value ?? "").Trim();
                    if (v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0)
                    {
                        MessageBox.Show("参数 " + e.Key + " 里有换行，请写在一行里。", "假人参数",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    e.Value = v;
                    _lines[e.Line] = e.Key + " = " + v;
                }
            }
            string text = string.Join("\n", _lines.ToArray());
            if (!text.EndsWith("\n")) text += "\n";
            File.WriteAllText(_tmp, text, new UTF8Encoding(false));

            Cursor = Cursors.WaitCursor;
            _lblStatus.ForeColor = TextDim;
            _lblStatus.Text = restart ? "正在保存并重启假人 ……" : "正在保存 ……";
            string err = "";
            string output = "";
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["text"] = text;
            bool ok = RobotApi.Call("robotConfigUpdate", body, out err, 60000) != null;
            if (ok && restart)
            {
                string e2;
                if (!RobotApi.RestartRobot(out e2)) { ok = false; output = "参数已保存，但重启假人失败：" + e2; }
            }
            if (!ok && output.Length == 0) output = err;
            Cursor = Cursors.Default;
            if (ok)
            {
                _lblStatus.ForeColor = OkColor;
                _lblStatus.Text = restart ? "已保存，假人已重启" : "已保存";
                MessageBox.Show(restart
                        ? "参数已保存，假人系统已用新参数重启完成。\n\n回游戏里看一下效果就行。"
                        : "参数已保存。\n\n注意：没重启假人时，部分参数要等假人下次启动才生效。",
                    "假人参数", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (restart) Close();
            }
            else
            {
                _lblStatus.ForeColor = BadColor;
                _lblStatus.Text = "保存失败";
                MessageBox.Show("保存失败：\n\n" + output.Trim(), "假人参数",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    /// 启动时的身份选择（左边：我是房主 / 右边：我不是房主）
    public class RoleForm : Form
    {
        static readonly Color Bg = Color.FromArgb(24, 26, 34);
        static readonly Color PanelBg = Color.FromArgb(32, 35, 46);
        static readonly Color PanelHover = Color.FromArgb(44, 50, 66);
        static readonly Color Accent = Color.FromArgb(64, 158, 255);
        static readonly Color TextMain = Color.FromArgb(232, 236, 244);
        static readonly Color TextDim = Color.FromArgb(140, 148, 166);

        public bool IsHost { get; private set; }

        public RoleForm()
        {
            Text = "DNF联机工具V1.0";
            ClientSize = new Size(380, 200);
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = new Label();
            title.Text = "DNF联机工具V1.0";
            title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            title.ForeColor = TextMain;
            title.AutoSize = true;
            title.Location = new Point(18, 14);
            Controls.Add(title);

            Controls.Add(MakeOption(16, 62, 164, 92, "我是房主", true,
                delegate { IsHost = true; DialogResult = DialogResult.OK; Close(); }));

            Controls.Add(MakeOption(200, 62, 164, 92, "我不是房主", false,
                delegate { IsHost = false; DialogResult = DialogResult.OK; Close(); }));
        }

        Panel MakeOption(int x, int y, int w, int h, string text, bool primary, EventHandler onClick)
        {
            Panel card = new Panel();
            card.Location = new Point(x, y);
            card.Size = new Size(w, h);
            card.BackColor = PanelBg;
            card.Cursor = Cursors.Hand;

            Label t = new Label();
            t.Text = text;
            t.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            t.ForeColor = primary ? Accent : TextMain;
            t.AutoSize = false;
            t.TextAlign = ContentAlignment.MiddleCenter;
            t.Dock = DockStyle.Fill;
            t.Cursor = Cursors.Hand;
            card.Controls.Add(t);

            EventHandler click = delegate(object s, EventArgs e) { onClick(s, e); };
            card.Click += click;
            t.Click += click;
            card.MouseEnter += delegate { card.BackColor = PanelHover; };
            card.MouseLeave += delegate { card.BackColor = PanelBg; };
            t.MouseEnter += delegate { card.BackColor = PanelHover; };
            t.MouseLeave += delegate { card.BackColor = PanelBg; };
            return card;
        }
    }

    /// 部署假人之前的设置窗口：服务端信息 + 路径 + 部署包 + WinSCP 检查
    public class DeploySetupForm : Form
    {
        static readonly Color Bg = Color.FromArgb(24, 26, 34);
        static readonly Color PanelBg = Color.FromArgb(32, 35, 46);
        static readonly Color Accent = Color.FromArgb(64, 158, 255);
        static readonly Color TextMain = Color.FromArgb(232, 236, 244);
        static readonly Color TextDim = Color.FromArgb(140, 148, 166);
        static readonly Color OkColor = Color.FromArgb(82, 196, 130);
        static readonly Color WarnColor = Color.FromArgb(230, 180, 80);

        TextBox _host, _port, _user, _pwd, _game, _client, _pkg;
        Label _lblCheck;

        public DeploySetupForm()
        {
            Text = "部署假人系统 · 先确认这些";
            ClientSize = new Size(660, 486);
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Microsoft YaHei UI", 9.5F);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = new Label();
            title.Text = "部署假人系统（robot）";
            title.Font = new Font("Microsoft YaHei UI", 12.5F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(18, 14);
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = "部署要连到你的服务端上传程序、再把密钥同步到本机客户端，所以先确认下面这些。";
            sub.ForeColor = TextDim;
            sub.AutoSize = true;
            sub.Location = new Point(18, 46);
            Controls.Add(sub);

            _host = AddRow("服务端主机", 84, 150, Cfg.VmIp);
            _port = AddRowRight("SSH 端口", 84, 340, 70, Cfg.SshPort.ToString());
            _user = AddRow("用户名", 122, 150, Cfg.SshUser);
            _pwd = AddRowRight("密码", 122, 340, 150, Cfg.SshPwd);
            _pwd.UseSystemPasswordChar = true;

            _game = AddRow("游戏服务端目录（远端）", 160, 300, Cfg.RemoteGameDir);

            _client = AddRow("客户端目录（本机）", 198, 296, Cfg.ClientDir);
            AddBrowse(496, 198, delegate
            {
                FolderBrowserDialog d = new FolderBrowserDialog();
                d.Description = "选择客户端目录（里面有登录器 exe）";
                if (Directory.Exists(_client.Text)) d.SelectedPath = _client.Text;
                if (d.ShowDialog(this) == DialogResult.OK) _client.Text = d.SelectedPath;
            });

            _pkg = AddRow("假人部署包目录", 236, 296, Cfg.RobotPkg);
            AddBrowse(496, 236, delegate
            {
                FolderBrowserDialog d = new FolderBrowserDialog();
                d.Description = "选择假人部署包目录（里面有 robot 和 config 文件夹）";
                if (Directory.Exists(_pkg.Text)) d.SelectedPath = _pkg.Text;
                if (d.ShowDialog(this) == DialogResult.OK) _pkg.Text = d.SelectedPath;
            });

            Label hint = new Label();
            hint.Text = "部署包里要有 robot 和 config\\conf\\config.ini；没有的话把 dofrobot 解压目录指给它。";
            hint.ForeColor = TextDim;
            hint.AutoSize = true;
            hint.Location = new Point(18, 266);
            Controls.Add(hint);

            _lblCheck = new Label();
            _lblCheck.AutoSize = false;
            _lblCheck.Size = new Size(624, 76);
            _lblCheck.Location = new Point(18, 292);
            _lblCheck.ForeColor = TextMain;
            Controls.Add(_lblCheck);

            Button ok = new RoundButton();
            ok.Text = "开始部署";
            ok.Size = new Size(140, 40);
            ok.Location = new Point(18, 424);
            ok.FlatStyle = FlatStyle.Flat;
            ok.FlatAppearance.BorderSize = 0;
            ok.BackColor = Accent;
            ok.ForeColor = Color.White;
            ok.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            ok.Cursor = Cursors.Hand;
            ok.Click += delegate { if (Validate_()) DialogResult = DialogResult.OK; };
            Controls.Add(ok);

            Button cancel = new RoundButton();
            cancel.Text = "取消";
            cancel.Size = new Size(96, 40);
            cancel.Location = new Point(174, 424);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderColor = Color.FromArgb(58, 62, 78);
            cancel.BackColor = PanelBg;
            cancel.ForeColor = TextMain;
            cancel.Cursor = Cursors.Hand;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);

            RefreshWinScpHint();
        }

        TextBox AddRow(string label, int y, int width, string value)
        {
            Label l = new Label();
            l.Text = label;
            l.AutoSize = true;
            l.ForeColor = TextDim;
            l.Location = new Point(18, y + 4);
            Controls.Add(l);

            TextBox tb = new TextBox();
            tb.Text = value;
            tb.Width = width;
            tb.Location = new Point(190, y);
            tb.BackColor = PanelBg;
            tb.ForeColor = TextMain;
            tb.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(tb);
            return tb;
        }

        TextBox AddRowRight(string label, int y, int labelLeft, int width, string value)
        {
            Label l = new Label();
            l.Text = label;
            l.AutoSize = true;
            l.ForeColor = TextDim;
            l.Location = new Point(labelLeft, y + 4);
            Controls.Add(l);

            TextBox tb = new TextBox();
            tb.Text = value;
            tb.Width = width;
            tb.Location = new Point(labelLeft + 96, y);
            tb.BackColor = PanelBg;
            tb.ForeColor = TextMain;
            tb.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(tb);
            return tb;
        }

        void AddBrowse(int x, int y, EventHandler onClick)
        {
            Button b = new RoundButton();
            b.Text = "浏览…";
            b.Size = new Size(70, 26);
            b.Location = new Point(x + 2, y - 1);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(58, 62, 78);
            b.BackColor = PanelBg;
            b.ForeColor = TextMain;
            b.Cursor = Cursors.Hand;
            b.Click += onClick;
            Controls.Add(b);
        }

        void RefreshWinScpHint()
        {
            string path = EnvCheckForm.FindWinScp();
            bool ok = path != null;
            _lblCheck.Text =
                (ok ? "√ 检测到 WinSCP：" + path : "！没检测到 WinSCP（装不装都不影响本工具）") + "\n" +
                "本工具用内置 SFTP 传文件，不用 WinSCP 也能完成部署；\n" +
                "想手动看文件的话，WinSCP 可去 https://winscp.net 下载（非必需）。";
            _lblCheck.ForeColor = ok ? OkColor : WarnColor;
        }

        bool Validate_()
        {
            if (_host.Text.Trim().Length == 0) { MessageBox.Show("服务端主机不能为空。", "检查一下"); return false; }
            int p;
            if (!int.TryParse(_port.Text.Trim(), out p) || p <= 0 || p > 65535) { MessageBox.Show("SSH 端口不对。", "检查一下"); return false; }
            if (_user.Text.Trim().Length == 0) { MessageBox.Show("用户名不能为空。", "检查一下"); return false; }
            if (_client.Text.Trim().Length == 0 || !Directory.Exists(_client.Text.Trim()))
            { MessageBox.Show("客户端目录不存在，先选对（里面要有登录器 exe）。", "检查一下"); return false; }
            if (_pkg.Text.Trim().Length == 0 || !Directory.Exists(_pkg.Text.Trim()))
            { MessageBox.Show("假人部署包目录不存在。", "检查一下"); return false; }
            string robot = Path.Combine(_pkg.Text.Trim(), "robot");
            if (!File.Exists(robot))
            { MessageBox.Show("部署包里没找到 robot 文件：\n" + robot, "检查一下"); return false; }

            Cfg.VmIp = _host.Text.Trim();
            Cfg.SshPort = p;
            Cfg.SshUser = _user.Text.Trim();
            Cfg.SshPwd = _pwd.Text;
            Cfg.RemoteGameDir = _game.Text.Trim().Length > 0 ? _game.Text.Trim() : "/home/neople/game";
            Cfg.ClientDir = _client.Text.Trim();
            Cfg.RobotPkg = _pkg.Text.Trim();
            Cfg.Save();
            return true;
        }
    }

    /// 运行环境自检：.NET / WebView2 / 内置 Python / WinSCP / VMware / Radmin
    public class EnvCheckForm : Form
    {
        static readonly Color Bg = Color.FromArgb(24, 26, 34);
        static readonly Color PanelBg = Color.FromArgb(32, 35, 46);
        static readonly Color TextMain = Color.FromArgb(232, 236, 244);
        static readonly Color TextDim = Color.FromArgb(140, 148, 166);
        static readonly Color OkColor = Color.FromArgb(82, 196, 130);
        static readonly Color BadColor = Color.FromArgb(235, 100, 100);
        static readonly Color WarnColor = Color.FromArgb(230, 180, 80);

        public EnvCheckForm()
        {
            Text = "环境自检";
            ClientSize = new Size(660, 460);
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Microsoft YaHei UI", 9.5F);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = new Label();
            title.Text = "运行环境自检";
            title.Font = new Font("Microsoft YaHei UI", 12.5F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(18, 14);
            Controls.Add(title);

            int y = 54;
            y = Row(y, ".NET Framework 4.8", DotNetVersion(), DotNetOk(),
                "Win10 1903+/Win11 自带；缺了去微软官网装 .NET Framework 4.8 运行时");
            y = Row(y, "内置 Python（本工具自带）", File.Exists(Cfg.Python) ? "已就绪" : "缺失", File.Exists(Cfg.Python),
                "python 子目录要和 exe 放在一起；缺了整个工具只能当朋友端用");
            y = Row(y, "WinSCP（可选，不影响使用）", FindWinScp() ?? "未安装", FindWinScp() != null,
                "本工具内置 SFTP，不需要 WinSCP");
            y = Row(y, "VMware Workstation（房主开服用）", File.Exists(Cfg.VmRun) ? "已安装" : "未找到", File.Exists(Cfg.VmRun),
                "房主起服务端要用 vmrun；朋友端不需要");
            string radmin = MainForm.DetectRadminIp();
            y = Row(y, "Radmin VPN 网卡", radmin ?? "未连接", radmin != null,
                "装 Radmin VPN 并加入同一个网络");

            Button ok = new RoundButton();
            ok.Text = "好";
            ok.Size = new Size(96, 36);
            ok.Location = new Point(18, 404);
            ok.FlatStyle = FlatStyle.Flat;
            ok.FlatAppearance.BorderColor = Color.FromArgb(58, 62, 78);
            ok.BackColor = PanelBg;
            ok.ForeColor = TextMain;
            ok.Cursor = Cursors.Hand;
            ok.Click += delegate { Close(); };
            Controls.Add(ok);

            Label tip = new Label();
            tip.Text = "朋友端只需要 .NET 4.8（系统自带），不需要 Python / VMware / WinSCP。";
            tip.ForeColor = TextDim;
            tip.AutoSize = true;
            tip.Location = new Point(130, 414);
            Controls.Add(tip);
        }

        int Row(int y, string name, string value, bool ok, string fix)
        {
            Label l = new Label();
            l.Text = (ok ? "√ " : "× ") + name;
            l.ForeColor = ok ? OkColor : BadColor;
            l.AutoSize = true;
            l.Location = new Point(18, y);
            Controls.Add(l);

            Label v = new Label();
            v.Text = value;
            v.ForeColor = TextMain;
            v.AutoSize = true;
            v.Location = new Point(300, y);
            Controls.Add(v);

            Label f = new Label();
            f.Text = fix;
            f.ForeColor = TextDim;
            f.AutoSize = false;
            f.Size = new Size(624, 18);
            f.Location = new Point(20, y + 20);
            Controls.Add(f);
            return y + 52;
        }

        public static string FindWinScp()
        {
            string[] cand =
            {
                @"D:\Program Files (x86)\WinSCP\WinSCP.exe",
                @"C:\Program Files (x86)\WinSCP\WinSCP.exe",
                @"C:\Program Files\WinSCP\WinSCP.exe"
            };
            foreach (string p in cand) if (File.Exists(p)) return p;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WinSCP_is1"))
                {
                    if (k != null)
                    {
                        string loc = k.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(loc) && File.Exists(Path.Combine(loc, "WinSCP.exe")))
                            return Path.Combine(loc, "WinSCP.exe");
                    }
                }
            }
            catch { }
            return null;
        }

        static string DotNetVersion()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (k == null) return "未安装";
                    object r = k.GetValue("Release");
                    if (r == null) return "未知";
                    int rel = Convert.ToInt32(r);
                    if (rel >= 533320) return "4.8.1+";
                    if (rel >= 528040) return "4.8";
                    if (rel >= 461808) return "4.7.2";
                    return "偏旧（Release=" + rel + "）";
                }
            }
            catch { return "读取失败"; }
        }

        static bool DotNetOk()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (k == null) return false;
                    object r = k.GetValue("Release");
                    return r != null && Convert.ToInt32(r) >= 528040;
                }
            }
            catch { return false; }
        }

        public static string WebViewVersion()
        {
            string[] keys =
            {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
            };
            foreach (string kp in keys)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(kp))
                    {
                        if (k != null)
                        {
                            string pv = k.GetValue("pv") as string;
                            if (!string.IsNullOrEmpty(pv) && pv != "0.0.0.0") return pv;
                        }
                    }
                }
                catch { }
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(kp))
                    {
                        if (k != null)
                        {
                            string pv = k.GetValue("pv") as string;
                            if (!string.IsNullOrEmpty(pv) && pv != "0.0.0.0") return pv;
                        }
                    }
                }
                catch { }
            }
            return null;
        }
    }

    // ================= 圆角按钮 =================
    public class RoundButton : Button
    {
        public int Radius = 10;

        bool _hover, _down;

        public RoundButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
        }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            if (rad <= 0 || r.Width <= 2 || r.Height <= 2) { p.AddRectangle(r); return p; }
            int d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs me) { _down = true; Invalidate(); base.OnMouseDown(me); }
        protected override void OnMouseUp(MouseEventArgs me) { _down = false; Invalidate(); base.OnMouseUp(me); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);

            Color fill = BackColor;
            if (_down) fill = Mix(fill, Color.Black, 0.18);
            else if (_hover) fill = Mix(fill, Color.White, 0.10);
            if (!Enabled) fill = Mix(fill, Color.Black, 0.25);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Round(r, Radius))
            {
                using (SolidBrush br = new SolidBrush(fill)) g.FillPath(br, path);
                int bs = FlatAppearance.BorderSize;
                if (bs > 0)
                {
                    Color bc = _hover ? Mix(FlatAppearance.BorderColor, Color.White, 0.18) : FlatAppearance.BorderColor;
                    using (Pen p = new Pen(bc, bs)) g.DrawPath(p, path);
                }
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            switch (TextAlign)
            {
                case ContentAlignment.MiddleLeft: flags |= TextFormatFlags.Left; break;
                case ContentAlignment.MiddleRight: flags |= TextFormatFlags.Right; break;
                default: flags |= TextFormatFlags.HorizontalCenter; break;
            }
            Rectangle tr = new Rectangle(Padding.Left + 2, 0,
                Width - Padding.Left - Padding.Right - 4, Height);
            TextRenderer.DrawText(g, Text, Font, tr, ForeColor, flags);
        }
    }

    // ================= 会报告滚动位置的文本框 =================
    public class ScrollTextBox : TextBox
    {
        public event EventHandler Scrolled;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (Scrolled != null &&
                (m.Msg == 0x0115 || m.Msg == 0x020A || m.Msg == 0x0100 || m.Msg == 0x0102 ||
                 m.Msg == 0x0114 || m.Msg == 0x00B6))
                Scrolled(this, EventArgs.Empty);
        }
    }

    // ================= 日志框（深色自绘滚动条） =================
    public class LogBox : Panel
    {
        public ScrollTextBox Box;

        readonly Panel _bar;
        Rectangle _thumb;
        bool _drag;
        bool _hoverBar;
        int _dragOff;
        const int BARW = 8;
        const int BARGAP = 6;

        [StructLayout(LayoutKind.Sequential)]
        struct SCROLLINFO
        {
            public uint cbSize;
            public uint fMask;
            public int nMin;
            public int nMax;
            public uint nPage;
            public int nPos;
            public int nTrackPos;
        }

        [DllImport("user32.dll")]
        static extern bool GetScrollInfo(IntPtr hWnd, int nBar, ref SCROLLINFO si);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        const int SB_VERT = 1;
        const int WM_VSCROLL = 0x0115;
        const uint SIF_ALL = 0x17;
        const int SB_LINEUP = 0;
        const int SB_LINEDOWN = 1;
        const int SB_THUMBTRACK = 5;

        public LogBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(24, 26, 34);
            Padding = new Padding(12, 8, 12, 8);

            Box = new ScrollTextBox();
            Box.Multiline = true;
            Box.ReadOnly = true;
            Box.ScrollBars = ScrollBars.Vertical;   // 保留系统滚动能力（内部会占用右侧 17px，由 _bar 盖住）
            Box.WordWrap = true;
            Box.BorderStyle = BorderStyle.None;
            Box.Dock = DockStyle.Fill;
            Box.BackColor = BackColor;
            Box.ForeColor = Color.FromArgb(200, 210, 226);
            Box.Scrolled += delegate { if (_bar != null) _bar.Invalidate(); };
            Controls.Add(Box);

            _bar = new Panel();
            _bar.BackColor = BackColor;
            _bar.Cursor = Cursors.Default;
            _bar.Paint += PaintBar;
            _bar.MouseDown += BarDown;
            _bar.MouseMove += BarMove;
            _bar.MouseUp += BarUp;
            _bar.MouseEnter += delegate { _hoverBar = true; _bar.Invalidate(); };
            _bar.MouseLeave += delegate { _hoverBar = false; _bar.Invalidate(); };
            Controls.Add(_bar);
            _bar.BringToFront();

            Box.SizeChanged += delegate { LayoutBar(); };
            LayoutBar();
        }

        void LayoutBar()
        {
            if (_bar == null || Box == null) return;
            int sbw = SystemInformation.VerticalScrollBarWidth;
            _bar.SetBounds(Box.Right - sbw, Box.Top, sbw, Box.Height);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutBar();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutBar();
        }

        SCROLLINFO Info()
        {
            SCROLLINFO si = new SCROLLINFO();
            si.cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO));
            si.fMask = SIF_ALL;
            GetScrollInfo(Box.Handle, SB_VERT, ref si);
            return si;
        }

        void PaintBar(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(_bar.BackColor)) g.FillRectangle(bg, _bar.ClientRectangle);

            SCROLLINFO si = Info();
            int trackTop = 4;
            int trackH = _bar.ClientSize.Height - 8;
            if (trackH < 20) { _thumb = Rectangle.Empty; return; }

            int bx = (_bar.ClientSize.Width - BARW) / 2;
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(28, 31, 41)))
            using (GraphicsPath tp = RoundButton.Round(new Rectangle(bx, trackTop, BARW, trackH), BARW / 2))
                g.FillPath(tb, tp);

            int span = Math.Max(1, si.nMax - si.nMin + 1);
            int page = (int)Math.Max(1, si.nPage);
            if (page >= span) { _thumb = Rectangle.Empty; return; }

            int thumbH = Math.Max(26, (int)((double)trackH * page / span));
            int scrolled = si.nPos - si.nMin;
            int maxScroll = Math.Max(1, span - page);
            int thumbY = trackTop + (int)((double)(trackH - thumbH) * Math.Min(scrolled, maxScroll) / maxScroll);
            _thumb = new Rectangle(bx, thumbY, BARW, thumbH);

            Color c = _drag ? Color.FromArgb(148, 158, 180)
                     : (_hoverBar ? Color.FromArgb(112, 122, 144) : Color.FromArgb(86, 94, 114));
            using (SolidBrush sb = new SolidBrush(c))
            using (GraphicsPath sp = RoundButton.Round(_thumb, BARW / 2))
                g.FillPath(sb, sp);
        }

        void ScrollTo(int pos)
        {
            SendMessage(Box.Handle, WM_VSCROLL, (IntPtr)((pos << 16) | SB_THUMBTRACK), IntPtr.Zero);
            _bar.Invalidate();
        }

        void BarDown(object sender, MouseEventArgs e)
        {
            if (_thumb.Contains(e.Location))
            {
                _drag = true;
                _dragOff = e.Y - _thumb.Y;
                _bar.Invalidate();
                return;
            }
            if (_thumb.IsEmpty) return;
            SendMessage(Box.Handle, WM_VSCROLL, (IntPtr)(e.Y < _thumb.Y ? SB_LINEUP : SB_LINEDOWN), IntPtr.Zero);
            for (int k = 0; k < 8; k++)
                SendMessage(Box.Handle, WM_VSCROLL, (IntPtr)(e.Y < _thumb.Y ? SB_LINEUP : SB_LINEDOWN), IntPtr.Zero);
            _bar.Invalidate();
        }

        void BarMove(object sender, MouseEventArgs e)
        {
            if (!_drag) return;
            SCROLLINFO si = Info();
            int trackTop = 4;
            int trackH = _bar.ClientSize.Height - 8;
            int span = Math.Max(1, si.nMax - si.nMin + 1);
            int page = (int)Math.Max(1, si.nPage);
            int thumbH = Math.Max(26, (int)((double)trackH * page / span));
            int space = Math.Max(1, trackH - thumbH);
            int y = e.Y - _dragOff - trackTop;
            if (y < 0) y = 0;
            if (y > space) y = space;
            int target = si.nMin + (int)((double)y / space * Math.Max(1, span - page));
            ScrollTo(target);
        }

        void BarUp(object sender, MouseEventArgs e)
        {
            _drag = false;
            _bar.Invalidate();
        }
    }

    /// 输入框（RobotForm 等非 MainForm 场景用）
    public static class InputBox
    {
        public static string Show(string title, string prompt, string def)
        {
            Form f = new Form();
            f.Text = title;
            f.ClientSize = new Size(460, 168);
            f.StartPosition = FormStartPosition.CenterParent;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.BackColor = Color.FromArgb(24, 26, 34);
            f.ForeColor = Color.FromArgb(232, 236, 244);
            f.Font = new Font("Microsoft YaHei UI", 9.5F);
            try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label l = new Label();
            l.Text = prompt;
            l.AutoSize = false;
            l.SetBounds(18, 16, 424, 44);
            l.ForeColor = Color.FromArgb(200, 206, 220);
            f.Controls.Add(l);

            TextBox t = new TextBox();
            t.Text = def ?? "";
            t.SetBounds(18, 64, 424, 28);
            t.BackColor = Color.FromArgb(32, 35, 46);
            t.ForeColor = Color.FromArgb(232, 236, 244);
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = new Font("Microsoft YaHei UI", 10F);
            f.Controls.Add(t);

            Button ok = new Button();
            ok.Text = "确定";
            ok.SetBounds(250, 108, 92, 36);
            ok.DialogResult = DialogResult.OK;
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = Color.FromArgb(64, 158, 255);
            ok.ForeColor = Color.White;
            ok.FlatAppearance.BorderSize = 0;
            f.Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(350, 108, 92, 36);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(32, 35, 46);
            cancel.ForeColor = Color.FromArgb(140, 148, 166);
            cancel.FlatAppearance.BorderColor = Color.FromArgb(48, 52, 66);
            f.Controls.Add(cancel);

            f.AcceptButton = ok;
            f.CancelButton = cancel;
            t.SelectAll();
            t.Focus();
            return f.ShowDialog() == DialogResult.OK ? t.Text.Trim() : null;
        }
    }

    /// 市场（拍卖行 / 点券寄售）：补货、收菜、清库、守卫、内存补丁
    public class MarketForm : Form
    {
        static readonly Color Bg = Color.FromArgb(24, 26, 34);
        static readonly Color PanelBg = Color.FromArgb(32, 35, 46);
        static readonly Color Accent = Color.FromArgb(64, 158, 255);
        static readonly Color TextMain = Color.FromArgb(232, 236, 244);
        static readonly Color TextDim = Color.FromArgb(140, 148, 166);
        static readonly Color OkColor = Color.FromArgb(82, 196, 130);
        static readonly Color BadColor = Color.FromArgb(235, 100, 100);
        static readonly Color WarnColor = Color.FromArgb(230, 180, 80);
        static readonly Color Line = Color.FromArgb(48, 52, 66);

        Label _lAuc, _lCera, _lKinds, _lStock, _lblSt;
        LogBox _logBox;
        bool _busy;

        public MarketForm()
        {
            Text = "假人市场 · 拍卖行 / 点券";
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(760, 520);
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildLog();
            BuildStatus();
            BuildToolbar();
            BuildCards();
            BuildHeader();

            Shown += delegate { RefreshAsync(); };
        }

        void BuildHeader()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Top;
            p.Height = 62;
            p.BackColor = Bg;
            p.Padding = new Padding(18, 10, 18, 6);

            Label t = new Label();
            t.Text = "假人市场";
            t.AutoSize = true;
            t.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            t.ForeColor = TextMain;
            t.Location = new Point(18, 8);
            p.Controls.Add(t);

            Label s = new Label();
            s.Text = "拍卖行 / 点券寄售的补货、收菜、清库与守卫（调试时先把【自动】关掉）";
            s.AutoSize = true;
            s.Font = new Font("Microsoft YaHei UI", 8.5F);
            s.ForeColor = TextDim;
            s.Location = new Point(20, 38);
            p.Controls.Add(s);
            Controls.Add(p);
        }

        Panel MakeCard(int idx, string key, out Label val)
        {
            Panel c = new Panel();
            c.SetBounds(idx * 190, 0, 180, 72);
            c.BackColor = PanelBg;
            Label k = new Label();
            k.Text = key;
            k.AutoSize = true;
            k.Font = new Font("Microsoft YaHei UI", 8.5F);
            k.ForeColor = TextDim;
            k.Location = new Point(12, 8);
            c.Controls.Add(k);
            Label v = new Label();
            v.Text = "-";
            v.AutoSize = false;
            v.SetBounds(12, 28, 156, 34);
            v.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
            v.ForeColor = TextMain;
            v.AutoEllipsis = true;
            c.Controls.Add(v);
            val = v;
            return c;
        }

        void BuildCards()
        {
            Panel host = new Panel();
            host.Dock = DockStyle.Top;
            host.Height = 80;
            host.BackColor = Bg;
            host.Padding = new Padding(18, 0, 18, 8);
            host.Controls.Add(MakeCard(0, "拍卖行服务", out _lAuc));
            host.Controls.Add(MakeCard(1, "点券服务", out _lCera));
            host.Controls.Add(MakeCard(2, "上架种类", out _lKinds));
            host.Controls.Add(MakeCard(3, "市场自动", out _lStock));
            Controls.Add(host);
        }

        void BuildToolbar()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Top;
            p.Height = 86;
            p.BackColor = Bg;
            p.Padding = new Padding(18, 6, 18, 6);

            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Dock = DockStyle.Fill;
            f.WrapContents = true;
            f.BackColor = Bg;

            f.Controls.Add(Btn("刷新", true, delegate { RefreshAsync(); }));
            f.Controls.Add(Btn("市场开始", false, delegate { Run("启动市场", "marketStart", null); }));
            f.Controls.Add(Btn("市场停止", false, delegate { Run("停止市场", "marketStop", null); }));
            f.Controls.Add(Btn("补货·拍卖", false, delegate { Restock("auction"); }));
            f.Controls.Add(Btn("补货·点券", false, delegate { Restock("cera"); }));
            f.Controls.Add(Btn("收菜·拍卖", false, delegate { Collect("auction"); }));
            f.Controls.Add(Btn("收菜·点券", false, delegate { Collect("cera"); }));
            f.Controls.Add(Btn("同步物品", false, delegate { Run("同步物品信息", "marketSyncItemInfo", null); }));
            f.Controls.Add(Btn("清系统库存", false, delegate { ClearStock(); }));
            f.Controls.Add(Btn("拍卖守卫", false, delegate { Run("安装拍卖守卫", "marketInstallAuctionGuard", null); }));
            f.Controls.Add(Btn("拍卖设置", true, delegate { OpenSettings(); }));
            f.Controls.Add(Btn("内存补丁", false, delegate { PatchMemory(); }));
            f.Controls.Add(Btn("网页版", false, delegate { try { Process.Start(RobotApi.Base + "/"); } catch { } }));

            p.Controls.Add(f);
            Controls.Add(p);
        }

        RoundButton Btn(string text, bool accent, EventHandler onClick)
        {
            RoundButton b = new RoundButton();
            b.Text = text;
            b.Width = 96;
            b.Height = 32;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = accent ? 0 : 1;
            b.FlatAppearance.BorderColor = Line;
            b.BackColor = accent ? Accent : PanelBg;
            b.ForeColor = accent ? Color.White : TextMain;
            b.Font = new Font("Microsoft YaHei UI", 9F, accent ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            b.Click += onClick;
            return b;
        }

        void BuildLog()
        {
            _logBox = new LogBox();
            _logBox.Dock = DockStyle.Fill;
            Panel wrap = new Panel();
            wrap.Dock = DockStyle.Fill;
            wrap.BackColor = Bg;
            wrap.Padding = new Padding(18, 6, 18, 6);
            wrap.Controls.Add(_logBox);
            Controls.Add(wrap);
        }

        void BuildStatus()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Bottom;
            p.Height = 30;
            p.BackColor = PanelBg;
            _lblSt = new Label();
            _lblSt.Text = "就绪";
            _lblSt.Dock = DockStyle.Fill;
            _lblSt.TextAlign = ContentAlignment.MiddleLeft;
            _lblSt.Padding = new Padding(14, 0, 0, 0);
            _lblSt.ForeColor = TextDim;
            p.Controls.Add(_lblSt);
            Controls.Add(p);
        }

        void Say(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action(delegate { Say(s); })); return; }
            _logBox.Box.AppendText(s + "\r\n");
        }

        void SetSt(string s, Color c)
        {
            if (InvokeRequired) { BeginInvoke(new Action(delegate { SetSt(s, c); })); return; }
            _lblSt.Text = s;
            _lblSt.ForeColor = c;
        }

        void RefreshAsync()
        {
            if (_busy) return;
            _busy = true;
            SetSt("正在读取市场状态 ……", TextDim);
            Task.Run(new Action(delegate
            {
                string e1, e2;
                Dictionary<string, object> st = RobotApi.Call("marketStatus", null, out e1, 30000);
                Dictionary<string, object> kd = RobotApi.Call("marketKindsProgress", null, out e2, 60000);
                BeginInvoke(new Action(delegate
                {
                    _busy = false;
                    if (st == null && kd == null)
                    {
                        SetSt("读取失败：" + e1, BadColor);
                        _lAuc.Text = "未连接";
                        _lAuc.ForeColor = BadColor;
                        return;
                    }
                    Dictionary<string, object> svc = RobotApi.Obj(st, "services");
                    Dictionary<string, object> a = RobotApi.Obj(svc, "auction");
                    Dictionary<string, object> c = RobotApi.Obj(svc, "point");
                    string sa = RobotApi.Str(a, "status", "-");
                    string sc = RobotApi.Str(c, "status", "-");
                    _lAuc.Text = sa;
                    _lAuc.ForeColor = sa == "ready" ? OkColor : BadColor;
                    _lCera.Text = sc;
                    _lCera.ForeColor = sc == "ready" ? OkColor : BadColor;

                    int actual = RobotApi.Int(kd, "actual", -1);
                    _lKinds.Text = actual >= 0 ? (actual + " 种") : "-";
                    _lKinds.ForeColor = TextMain;

                    bool mauto = RobotApi.Bool(st, "auto_running", false);
                    _lStock.Text = mauto ? "On" : "Off";
                    _lStock.ForeColor = mauto ? OkColor : WarnColor;

                    SetSt("已刷新 " + DateTime.Now.ToString("HH:mm:ss"), TextDim);
                    Say("[刷新] 拍卖=" + sa + "  点券=" + sc + "  上架种类=" + _lKinds.Text + "  市场自动=" + _lStock.Text);
                }));
            }));
        }

        void Run(string title, string command, Dictionary<string, object> payload)
        {
            SetSt(title + " ……", TextDim);
            Task.Run(new Action(delegate
            {
                string e;
                Dictionary<string, object> r = RobotApi.Call(command, payload, out e, 300000);
                BeginInvoke(new Action(delegate
                {
                    if (r == null) { SetSt(title + "失败：" + e, BadColor); Say("[失败] " + title + "：" + e); }
                    else { SetSt(title + " 完成", OkColor); Say("[完成] " + title + "  " + Short(RobotApi.Js.Serialize(r))); }
                }));
            }));
        }

        static string Short(string s)
        {
            if (s == null) return "";
            return s.Length > 320 ? s.Substring(0, 320) + " …" : s;
        }

        void Restock(string market)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["market"] = market;
            body["continue_on_error"] = true;
            Run("补货（" + market + "）", "marketRestockOnce", body);
        }

        void Collect(string market)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["market"] = market;
            body["continue_on_error"] = true;
            Run("收菜（" + market + "）", "marketCollectOnce", body);
        }

        void ClearStock()
        {
            if (MessageBox.Show("确定清空系统库存吗？\n\n建议流程：先把【自动】关掉 → 清库 → 重启 auction/point → 内存补丁 → 补货。",
                "清空系统库存", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Task.Run(new Action(delegate
            {
                Run("停止市场", "marketStop", null);
            }));
            Run("清空系统库存", "marketClearSystemStock", null);
        }

        void OpenSettings()
        {
            try
            {
                MarketSettingsForm f = new MarketSettingsForm(null);
                f.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开拍卖设置失败：\n" + ex.Message, "拍卖设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void PatchMemory()
        {
            if (MessageBox.Show("确定对游戏服务端进程打内存补丁吗？\n\n会修改 auction/point 进程的内存，务必在没人交易的时候做。",
                "内存补丁", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Run("市场内存补丁", "marketPatchAuctionMemory", null);
        }
    }

    public class MarketSettingsForm : Form
    {
        static readonly Color Bg = Color.FromArgb(24, 26, 34);
        static readonly Color PanelBg = Color.FromArgb(32, 35, 46);
        static readonly Color Accent = Color.FromArgb(64, 158, 255);
        static readonly Color TextMain = Color.FromArgb(232, 236, 244);
        static readonly Color TextDim = Color.FromArgb(140, 148, 166);
        static readonly Color OkColor = Color.FromArgb(82, 196, 130);
        static readonly Color BadColor = Color.FromArgb(235, 100, 100);
        static readonly Color Line = Color.FromArgb(48, 52, 66);

        static readonly string[] CatKeys = new string[] { "equipment", "title", "card", "creature", "artifact", "bead", "recipe", "material", "consumable", "puppet", "other" };
        static readonly string[] CatNames = new string[] { "装备", "称号", "卡片", "宠物", "宠物装备", "宝珠", "设计图", "材料", "药剂与消耗品", "人偶", "其他 / 未分类" };

        TextBox _inRange, _outRange, _randLow, _randHigh, _equipMax, _lvMin, _lvMax, _upMin, _upMax, _stack;
        CheckBox _chkCollect, _chkRange;
        Dictionary<string, TextBox[]> _cats = new Dictionary<string, TextBox[]>();
        Dictionary<string, object> _rules = new Dictionary<string, object>();
        Panel _body;
        Label _lblSt;
        int _y;

        public MarketSettingsForm(Dictionary<string, object> status)
        {
            Text = "假人市场 · 拍卖设置";
            ClientSize = new Size(780, 720);
            MinimumSize = new Size(700, 560);
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildBottom();
            BuildBody();
            BuildHeader(status);
            if (status != null) Fill(status);
            else Shown += delegate { Reload(); };
        }

        void BuildHeader(Dictionary<string, object> status)
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Top;
            p.Height = 64;
            p.BackColor = Bg;
            Label t = new Label();
            t.Text = "拍卖设置";
            t.AutoSize = true;
            t.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            t.ForeColor = TextMain;
            t.Location = new Point(18, 10);
            p.Controls.Add(t);
            Label s = new Label();
            s.Text = "对应服务端 /root/config/conf/market_config.ini（与网页端「拍卖」对话框一致）";
            s.AutoSize = true;
            s.Font = new Font("Microsoft YaHei UI", 8.5F);
            s.ForeColor = TextDim;
            s.Location = new Point(20, 40);
            p.Controls.Add(s);
            Controls.Add(p);
        }

        void BuildBody()
        {
            _body = new Panel();
            _body.Dock = DockStyle.Fill;
            _body.AutoScroll = true;
            _body.BackColor = Bg;
            _body.Padding = new Padding(18, 6, 18, 10);
            Controls.Add(_body);
        }

        void BuildBottom()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Bottom;
            p.Height = 58;
            p.BackColor = PanelBg;
            p.Padding = new Padding(18, 10, 18, 10);

            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Dock = DockStyle.Left;
            f.Width = 420;
            f.WrapContents = false;
            f.Controls.Add(Btn("保存", false, delegate { Save(false); }));
            f.Controls.Add(Btn("保存并重建", true, delegate { Save(true); }));
            f.Controls.Add(Btn("重新读取", false, delegate { Reload(); }));
            p.Controls.Add(f);

            _lblSt = new Label();
            _lblSt.Text = "就绪";
            _lblSt.Dock = DockStyle.Fill;
            _lblSt.TextAlign = ContentAlignment.MiddleRight;
            _lblSt.ForeColor = TextDim;
            p.Controls.Add(_lblSt);
            Controls.Add(p);
        }

        RoundButton Btn(string text, bool accent, EventHandler onClick)
        {
            RoundButton b = new RoundButton();
            b.Text = text;
            b.Width = 120;
            b.Height = 34;
            b.Margin = new Padding(0, 0, 8, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = accent ? 0 : 1;
            b.FlatAppearance.BorderColor = Line;
            b.BackColor = accent ? Accent : PanelBg;
            b.ForeColor = accent ? Color.White : TextMain;
            b.Font = new Font("Microsoft YaHei UI", 9F, accent ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            b.Click += onClick;
            return b;
        }

        void Group(string title)
        {
            Panel g = new Panel();
            g.SetBounds(0, _y, _body.ClientSize.Width - 40, 28);
            g.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Label l = new Label();
            l.Text = title;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.BottomLeft;
            l.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            l.ForeColor = Accent;
            g.Controls.Add(l);
            _body.Controls.Add(g);
            _y += 30;
        }

        TextBox Num(string label, string value, int width)
        {
            Label l = new Label();
            l.Text = label;
            l.AutoSize = false;
            l.SetBounds(4, _y + 4, 150, 22);
            l.ForeColor = TextDim;
            _body.Controls.Add(l);

            TextBox t = new TextBox();
            t.SetBounds(160, _y, width, 24);
            t.Text = value;
            t.BackColor = PanelBg;
            t.ForeColor = TextMain;
            t.BorderStyle = BorderStyle.FixedSingle;
            _body.Controls.Add(t);
            _y += 30;
            return t;
        }

        CheckBox Chk(string label, bool value)
        {
            CheckBox c = new CheckBox();
            c.Text = label;
            c.SetBounds(4, _y, 320, 24);
            c.ForeColor = TextMain;
            c.Checked = value;
            _body.Controls.Add(c);
            _y += 30;
            return c;
        }

        static string Fmt(double v)
        {
            if (Math.Abs(v - Math.Round(v)) < 0.0001) return ((long)Math.Round(v)).ToString();
            return v.ToString("0.####");
        }

        void Fill(Dictionary<string, object> st)
        {
            _body.SuspendLayout();
            _body.Controls.Clear();
            _cats.Clear();
            _y = 6;
            _rules = RobotApi.Obj(RobotApi.Obj(st, "restock"), "category_price_rules");
            if (_rules == null) _rules = new Dictionary<string, object>();

            Dictionary<string, object> col = RobotApi.Obj(st, "collector");
            Dictionary<string, object> res = RobotApi.Obj(st, "restock");

            Group("回收策略（玩家挂单的自动收购）");
            _chkCollect = Chk("自动回收（collector_enabled）", RobotApi.Bool(col, "enabled", true));
            _chkRange = Chk("物品独立价格范围（price_range_enabled）", RobotApi.Bool(col, "price_range_enabled", true));
            _inRange = Num("范围内回收概率 %", Fmt(RobotApi.Dbl(col, "in_range_probability", 0.8) * 100), 90);
            _outRange = Num("范围外回收概率 %", Fmt(RobotApi.Dbl(col, "out_of_range_probability", 0.1) * 100), 90);

            Group("价格浮动");
            _randLow = Num("最终价格浮动 下限 %", Fmt(RobotApi.Dbl(res, "rand_low", 0.9) * 100), 90);
            _randHigh = Num("最终价格浮动 上限 %", Fmt(RobotApi.Dbl(res, "rand_high", 1.05) * 100), 90);
            _equipMax = Num("装备最终价上限（金币）", RobotApi.Int(res, "equipment_final_max_price", 50000000).ToString(), 140);

            Group("装备 / 堆叠");
            _lvMin = Num("装备等级下限", RobotApi.Int(res, "equipment_level_min", 1).ToString(), 90);
            _lvMax = Num("装备等级上限", RobotApi.Int(res, "equipment_level_max", 127).ToString(), 90);
            _upMin = Num("强化下限", RobotApi.Int(res, "upgrade_min", 0).ToString(), 90);
            _upMax = Num("强化上限", RobotApi.Int(res, "upgrade_max", 12).ToString(), 90);
            string stack = "";
            List<object> ss = RobotApi.Arr(res, "stack_sizes");
            if (ss != null)
            {
                for (int i = 0; i < ss.Count; i++) stack += (i > 0 ? "," : "") + Convert.ToString(ss[i]);
            }
            _stack = Num("堆叠尺寸（逗号分隔）", stack, 200);

            Group("分类单价范围（上架价格，最小 ~ 最大）");
            for (int i = 0; i < CatKeys.Length; i++)
            {
                Dictionary<string, object> rule = RobotApi.Obj(_rules, CatKeys[i]);
                Label l = new Label();
                l.Text = CatNames[i];
                l.AutoSize = false;
                l.SetBounds(4, _y + 4, 120, 22);
                l.ForeColor = TextDim;
                _body.Controls.Add(l);

                TextBox tmin = new TextBox();
                tmin.SetBounds(130, _y, 110, 24);
                tmin.Text = RobotApi.Int(rule, "min_price", 0).ToString();
                tmin.BackColor = PanelBg;
                tmin.ForeColor = TextMain;
                tmin.BorderStyle = BorderStyle.FixedSingle;
                _body.Controls.Add(tmin);

                Label w = new Label();
                w.Text = "~";
                w.AutoSize = false;
                w.SetBounds(246, _y + 4, 16, 22);
                w.ForeColor = TextDim;
                _body.Controls.Add(w);

                TextBox tmax = new TextBox();
                tmax.SetBounds(264, _y, 110, 24);
                tmax.Text = RobotApi.Int(rule, "max_price", 0).ToString();
                tmax.BackColor = PanelBg;
                tmax.ForeColor = TextMain;
                tmax.BorderStyle = BorderStyle.FixedSingle;
                _body.Controls.Add(tmax);

                _cats[CatKeys[i]] = new TextBox[] { tmin, tmax };
                _y += 30;
            }
            _y += 6;
            _body.ResumeLayout();
            SetSt("已读取", TextDim);
        }

        void SetSt(string s, Color c)
        {
            if (InvokeRequired) { BeginInvoke(new Action(delegate { SetSt(s, c); })); return; }
            _lblSt.Text = s;
            _lblSt.ForeColor = c;
        }

        void Reload()
        {
            SetSt("正在读取 ……", TextDim);
            Task.Run(new Action(delegate
            {
                string e;
                Dictionary<string, object> st = RobotApi.Call("marketStatus", null, out e, 30000);
                BeginInvoke(new Action(delegate
                {
                    if (st == null) { SetSt("读取失败：" + e, BadColor); return; }
                    Fill(st);
                    SetSt("已重新读取", OkColor);
                }));
            }));
        }

        static bool TryInt(string s, out int v)
        {
            return int.TryParse((s ?? "").Trim(), out v);
        }

        static bool TryDbl(string s, out double v)
        {
            return double.TryParse((s ?? "").Trim(), out v);
        }

        Dictionary<string, object> BuildPayload(out string error)
        {
            error = null;
            int inP, outP, rl, rh, emax, lvMin, lvMax, upMin, upMax;
            if (!TryInt(_inRange.Text, out inP) || inP < 0 || inP > 100) { error = "范围内回收概率要填 0~100"; return null; }
            if (!TryInt(_outRange.Text, out outP) || outP < 0 || outP > 100) { error = "范围外回收概率要填 0~100"; return null; }
            if (!TryInt(_randLow.Text, out rl) || !TryInt(_randHigh.Text, out rh)) { error = "价格浮动要填整数百分比"; return null; }
            if (!TryInt(_equipMax.Text, out emax)) { error = "装备最终价上限要填整数"; return null; }
            if (!TryInt(_lvMin.Text, out lvMin) || !TryInt(_lvMax.Text, out lvMax)) { error = "装备等级要填整数"; return null; }
            if (!TryInt(_upMin.Text, out upMin) || !TryInt(_upMax.Text, out upMax)) { error = "强化范围要填整数"; return null; }

            List<int> stacks = new List<int>();
            string[] parts = (_stack.Text ?? "").Split(new char[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                int v;
                if (!int.TryParse(parts[i], out v)) { error = "堆叠尺寸只能填数字，用逗号分隔"; return null; }
                stacks.Add(v);
            }

            Dictionary<string, object> rules = new Dictionary<string, object>();
            for (int i = 0; i < CatKeys.Length; i++)
            {
                int mn, mx;
                if (!TryInt(_cats[CatKeys[i]][0].Text, out mn) || !TryInt(_cats[CatKeys[i]][1].Text, out mx))
                {
                    error = CatNames[i] + " 的价格要填整数";
                    return null;
                }
                Dictionary<string, object> src = RobotApi.Obj(_rules, CatKeys[i]);
                Dictionary<string, object> r = new Dictionary<string, object>();
                r["min_price"] = mn;
                r["max_price"] = mx;
                r["rarity_weight"] = RobotApi.Dbl(src, "rarity_weight", 1.0);
                r["level_weight"] = RobotApi.Dbl(src, "level_weight", 1.0);
                r["pvf_weight"] = RobotApi.Dbl(src, "pvf_weight", 1.0);
                string curve = RobotApi.Str(src, "rarity_score_curve", "");
                if (curve != null && curve.Length > 0) r["rarity_score_curve"] = curve;
                rules[CatKeys[i]] = r;
            }

            Dictionary<string, object> body = new Dictionary<string, object>();
            body["collector_enabled"] = _chkCollect.Checked;
            body["price_range_enabled"] = _chkRange.Checked;
            body["in_range_probability"] = inP / 100.0;
            body["out_of_range_probability"] = outP / 100.0;
            body["rand_low"] = rl / 100.0;
            body["rand_high"] = rh / 100.0;
            body["equipment_final_max_price"] = emax;
            body["equipment_level_min"] = lvMin;
            body["equipment_level_max"] = lvMax;
            body["upgrade_min"] = upMin;
            body["upgrade_max"] = upMax;
            if (stacks.Count > 0) body["stack_sizes"] = stacks;
            body["category_price_rules"] = rules;
            return body;
        }

        void Save(bool rebuild)
        {
            string err;
            Dictionary<string, object> body = BuildPayload(out err);
            if (body == null) { SetSt(err, BadColor); MessageBox.Show(err, "拍卖设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string cmd = rebuild ? "marketApplyListingConfig" : "marketConfigUpdate";
            SetSt(rebuild ? "正在保存并重建 ……" : "正在保存 ……", TextDim);
            Task.Run(new Action(delegate
            {
                string e;
                Dictionary<string, object> r = RobotApi.Call(cmd, body, out e, 300000);
                BeginInvoke(new Action(delegate
                {
                    if (r == null) { SetSt("保存失败：" + e, BadColor); MessageBox.Show("保存失败：\n" + e, "拍卖设置", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                    SetSt(rebuild ? "已保存并重建" : "已保存", OkColor);
                    MessageBox.Show(rebuild ? "已保存，正在按新规则重建挂单。\n（重建期间市场会停一下，跑完自动恢复）" : "已保存。\n（价格规则要生效请用【保存并重建】）", "拍卖设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            }));
        }
    }

}
