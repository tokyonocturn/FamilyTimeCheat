using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FamilyTimeChef.Studio
{
    internal sealed class MainForm : Form
    {
        private readonly Backend _backend = new Backend();

        private Label _connDot;
        private Label _connText;
        private Label _buildTitle;
        private Label _buildDetail;
        private Label _stats;

        private Button _btnConnect;
        private Button _btnSpawn;
        private Button _btnFeed;
        private Button _btnWater;

        private CheckBox _chkAutoFeed;
        private CheckBox _chkAutoWater;
        private CheckBox _chkAutoSpawn;
        private NumericUpDown _numFeed;
        private NumericUpDown _numWater;
        private NumericUpDown _numSpawn;

        private DataGridView _grid;
        private RichTextBox _log;
        private Timer _poll;

        private bool _suppressEvents;
        private SplitContainer _split;

        public MainForm()
        {
            Text = "Family Time Chef — Studio";
            ClientSize = new Size(1120, 700);
            MinimumSize = new Size(880, 560);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            BuildUi();
            WireBackend();

            _poll = new Timer { Interval = 1500 };
            _poll.Tick += (s, e) => Poll();
            _poll.Start();

            Shown += (s, e) =>
            {
                TryResizeSplit();
                TryAutoConnect();
            };
            FormClosing += (s, e) => { _poll?.Stop(); _backend.Dispose(); };
        }

        private void BuildUi()
        {
            // ---------- header ----------
            var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Panel, Padding = new Padding(20, 0, 20, 0) };

            var title = new Label
            {
                Text = "Family Time Chef",
                Font = Theme.H1,
                ForeColor = Theme.Text,
                AutoSize = true,
                Location = new Point(20, 16),
                BackColor = Color.Transparent
            };

            var subtitle = new Label
            {
                Text = "External companion • food, feeding and water automation",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(22, 44),
                BackColor = Color.Transparent
            };

            _connText = new Label
            {
                Text = "Disconnected",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight
            };

            _connDot = new Label
            {
                Text = "●",
                Font = new Font("Segoe UI", 12f),
                ForeColor = Theme.Warn,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            _btnConnect = Theme.Secondary("Connect");
            _btnConnect.Width = 110;
            _btnConnect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnConnect.Click += (s, e) => ToggleConnection();

            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(_connText);
            header.Controls.Add(_connDot);
            header.Controls.Add(_btnConnect);
            header.Resize += (s, e) => LayoutHeader(header);

            // ---------- left column ----------
            var left = new Panel { Dock = DockStyle.Left, Width = 360, BackColor = Theme.Bg, Padding = new Padding(20, 16, 10, 16), AutoScroll = true };

            var buildCard = Card("Detected game", 118);
            _buildTitle = new Label
            {
                Text = "Not connected",
                Font = Theme.H2,
                ForeColor = Theme.Text,
                Location = new Point(14, 34),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _buildDetail = new Label
            {
                Text = "Start Family Time with the plugin installed.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Location = new Point(14, 60),
                Size = new Size(290, 48),
                BackColor = Color.Transparent
            };
            buildCard.Controls.Add(_buildTitle);
            buildCard.Controls.Add(_buildDetail);

            _stats = new Label
            {
                Text = "",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Location = new Point(14, 96),
                Size = new Size(290, 20),
                BackColor = Color.Transparent
            };
            buildCard.Controls.Add(_stats);

            var manualCard = Card("Manual actions", 178);
            _btnSpawn = Theme.Primary("Spawn food");
            _btnSpawn.Location = new Point(14, 32);
            _btnSpawn.Size = new Size(302, 40);
            _btnSpawn.Click += (s, e) => _backend.Send("SPAWN");

            _btnFeed = Theme.Secondary("Feed hungry children now");
            _btnFeed.Location = new Point(14, 82);
            _btnFeed.Size = new Size(302, 36);
            _btnFeed.Click += (s, e) => _backend.Send("FEED");

            _btnWater = Theme.Secondary("Give water now");
            _btnWater.Location = new Point(14, 126);
            _btnWater.Size = new Size(302, 36);
            _btnWater.Click += (s, e) => _backend.Send("WATER");

            manualCard.Controls.Add(_btnSpawn);
            manualCard.Controls.Add(_btnFeed);
            manualCard.Controls.Add(_btnWater);

            var autoCard = Card("Automation", 232);

            _chkAutoFeed = Theme.Toggle("Automatic feeding");
            _chkAutoFeed.Location = new Point(14, 32);
            _chkAutoFeed.CheckedChanged += (s, e) => PushConfig();

            _numFeed = IntervalBox(14, 58);
            _numFeed.ValueChanged += (s, e) => PushConfig();

            _chkAutoWater = Theme.Toggle("Automatic water delivery");
            _chkAutoWater.Location = new Point(14, 92);
            _chkAutoWater.CheckedChanged += (s, e) => PushConfig();

            _numWater = IntervalBox(14, 118);
            _numWater.ValueChanged += (s, e) => PushConfig();

            _chkAutoSpawn = Theme.Toggle("Automatic food spawning");
            _chkAutoSpawn.Location = new Point(14, 152);
            _chkAutoSpawn.CheckedChanged += (s, e) => PushConfig();

            _numSpawn = IntervalBox(14, 178);
            _numSpawn.ValueChanged += (s, e) => PushConfig();

            autoCard.Controls.Add(_chkAutoFeed);
            autoCard.Controls.Add(_numFeed);
            autoCard.Controls.Add(_chkAutoWater);
            autoCard.Controls.Add(_numWater);
            autoCard.Controls.Add(_chkAutoSpawn);
            autoCard.Controls.Add(_numSpawn);

            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Theme.Bg
            };
            stack.Controls.Add(buildCard);
            stack.Controls.Add(manualCard);
            stack.Controls.Add(autoCard);
            left.Controls.Add(stack);

            // ---------- right column ----------
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 8,
                BackColor = Theme.Bg
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Theme.Panel,
                BorderStyle = BorderStyle.None,
                GridColor = Theme.Border,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false,
                Font = Theme.Body
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.PanelAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.PanelAlt;
            _grid.ColumnHeadersDefaultCellStyle.Font = Theme.H2;
            _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            _grid.ColumnHeadersHeight = 34;
            _grid.DefaultCellStyle.BackColor = Theme.Panel;
            _grid.DefaultCellStyle.ForeColor = Theme.Text;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(46, 52, 63);
            _grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            _grid.RowTemplate.Height = 28;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Group", FillWeight = 55 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "count", HeaderText = "Count", FillWeight = 15 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "hungry", HeaderText = "Needing care", FillWeight = 18 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "template", HeaderText = "Template", FillWeight = 12 });

            var gridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 4), BackColor = Theme.Bg };
            var gridTitle = new Label
            {
                Text = "Child needs",
                Font = Theme.H2,
                ForeColor = Theme.Text,
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = Color.Transparent
            };
            gridWrap.Controls.Add(_grid);
            gridWrap.Controls.Add(gridTitle);
            split.Panel1.Controls.Add(gridWrap);

            _log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Panel,
                ForeColor = Theme.Muted,
                BorderStyle = BorderStyle.None,
                Font = Theme.Mono,
                ReadOnly = true,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            var logWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = Theme.Bg };
            var logTitle = new Label
            {
                Text = "Operation log",
                Font = Theme.H2,
                ForeColor = Theme.Text,
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = Color.Transparent
            };
            logWrap.Controls.Add(_log);
            logWrap.Controls.Add(logTitle);
            split.Panel2.Controls.Add(logWrap);

            _split = split;

            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 16, 20, 16), BackColor = Theme.Bg };
            right.Controls.Add(split);

            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(header);

            SetEnabledState(false);
        }

        private static Panel Card(string title, int height)
        {
            var p = new Panel
            {
                Width = 330,
                Height = height,
                BackColor = Theme.Panel,
                Margin = new Padding(0, 0, 0, 14),
                Padding = new Padding(0)
            };

            var t = new Label
            {
                Text = title,
                Font = Theme.H2,
                ForeColor = Theme.Muted,
                Location = new Point(14, 10),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            p.Controls.Add(t);
            return p;
        }

        private static NumericUpDown IntervalBox(int x, int y)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x + 22, y),
                Width = 120,
                Minimum = 200,
                Maximum = 600000,
                Increment = 250,
                Value = 2500,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Body,
                ThousandsSeparator = false
            };
            return n;
        }

        private void TryResizeSplit()
        {
            try
            {
                if (_split == null) return;
                int h = _split.Height;
                if (h <= 0) return;
                _split.Panel1MinSize = 120;
                _split.Panel2MinSize = 120;
                int target = (int)(h * 0.56f);
                int max = h - _split.Panel2MinSize - _split.SplitterWidth;
                if (max < _split.Panel1MinSize) return;
                _split.SplitterDistance = Math.Max(_split.Panel1MinSize, Math.Min(target, max));
            }
            catch { }
        }

        private void LayoutHeader(Panel header)
        {
            _btnConnect.Location = new Point(header.Width - _btnConnect.Width - 20, 18);
            _connDot.Location = new Point(_btnConnect.Left - 26, 26);
            _connText.Location = new Point(_connDot.Left - 150, 30);
            _connText.Width = 146;
            _connText.TextAlign = ContentAlignment.MiddleRight;
        }

        private void WireBackend()
        {
            _backend.ConnectionChanged += (ok, msg) => Ui(() =>
            {
                _connDot.ForeColor = ok ? Theme.Good : Theme.Warn;
                _connText.Text = ok ? "Connected" : "Disconnected";
                _connText.ForeColor = ok ? Theme.Good : Theme.Muted;
                _btnConnect.Text = ok ? "Disconnect" : "Connect";
                SetEnabledState(ok);
                if (!string.IsNullOrEmpty(msg)) AppendLog(msg);
            });

            _backend.StatusReceived += f => Ui(() => ApplyStatus(f));

            _backend.LogReceived += msg => Ui(() => AppendLog(msg));

            _backend.GroupsReceived += rows => Ui(() =>
            {
                _grid.Rows.Clear();
                foreach (var r in rows)
                {
                    int idx = _grid.Rows.Add(r.Name, r.Count, r.Hungry, r.Template);
                    if (r.Hungry > 0) _grid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Accent;
                }
            });
        }

        private void ApplyStatus(Dictionary<string, string> f)
        {
            string ep, build, renderer, supported, note, inst, hungry, type;
            f.TryGetValue("episodeLabel", out ep);
            f.TryGetValue("buildLabel", out build);
            f.TryGetValue("renderer", out renderer);
            f.TryGetValue("supported", out supported);
            f.TryGetValue("note", out note);
            f.TryGetValue("instances", out inst);
            f.TryGetValue("hungry", out hungry);
            f.TryGetValue("hungerType", out type);

            _buildTitle.Text = string.IsNullOrEmpty(ep) ? "Unknown build" : ep + " • " + build;
            _buildTitle.ForeColor = supported == "1" ? Theme.Text : Theme.Warn;

            var detail = string.IsNullOrEmpty(renderer) ? "" : renderer;
            if (!string.IsNullOrEmpty(note)) detail += (detail.Length > 0 ? "\n" : "") + note;
            _buildDetail.Text = detail;

            _stats.Text = $"Instances: {inst}    Needing care: {hungry}    Need: {type}";

            _suppressEvents = true;
            try
            {
                _chkAutoFeed.Checked = Get(f, "autoFeed") == "1";
                _chkAutoWater.Checked = Get(f, "autoWater") == "1";
                _chkAutoSpawn.Checked = Get(f, "autoSpawn") == "1";
                SetNum(_numFeed, f, "feedMs");
                SetNum(_numWater, f, "waterMs");
                SetNum(_numSpawn, f, "spawnMs");
            }
            finally { _suppressEvents = false; }
        }

        private static string Get(Dictionary<string, string> f, string k)
        {
            string v;
            return f.TryGetValue(k, out v) ? v : string.Empty;
        }

        private static void SetNum(NumericUpDown n, Dictionary<string, string> f, string k)
        {
            var s = Get(f, k);
            decimal d;
            if (decimal.TryParse(s, out d))
            {
                if (d < n.Minimum) d = n.Minimum;
                if (d > n.Maximum) d = n.Maximum;
                n.Value = d;
            }
        }

        private void PushConfig()
        {
            if (_suppressEvents || !_backend.Connected) return;
            _backend.Send("CONFIG",
                "autoFeed", _chkAutoFeed.Checked ? "1" : "0",
                "autoWater", _chkAutoWater.Checked ? "1" : "0",
                "autoSpawn", _chkAutoSpawn.Checked ? "1" : "0",
                "feedMs", ((int)_numFeed.Value).ToString(),
                "waterMs", ((int)_numWater.Value).ToString(),
                "spawnMs", ((int)_numSpawn.Value).ToString());
        }

        private void Poll()
        {
            if (!_backend.Connected) return;
            _backend.Send("STATUS");
            _backend.Send("GROUPS");
        }

        private void TryAutoConnect()
        {
            string err;
            if (_backend.Connect(out err))
            {
                AppendLog("Connected to the running game.");
            }
            else
            {
                AppendLog("Not connected: " + err);
            }
        }

        private void ToggleConnection()
        {
            if (_backend.Connected)
            {
                _backend.Disconnect();
            }
            else
            {
                TryAutoConnect();
            }
        }

        private void SetEnabledState(bool on)
        {
            _btnSpawn.Enabled = on;
            _btnFeed.Enabled = on;
            _btnWater.Enabled = on;
            _chkAutoFeed.Enabled = on;
            _chkAutoWater.Enabled = on;
            _chkAutoSpawn.Enabled = on;
            _numFeed.Enabled = on;
            _numWater.Enabled = on;
            _numSpawn.Enabled = on;
        }

        private void AppendLog(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        private void Ui(Action action)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { }
        }
    }
}
