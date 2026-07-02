using Microsoft.Win32;
using System.Runtime.InteropServices;

// 备注：由于是单文件轻量应用，因此各种function函数没有作拆分，后续有更多需求再考虑拆分成多个类文件。

namespace VirtualScreenSwitch
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        // ===== Win32 API =====
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int hMsg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MONITORPOWER = 0xF170;

        // ===== 状态机（对应原 bat 的三种循环）=====
        private enum State { Main, DelayLock, Paused }
        private enum Lang { ZH, EN }

        private State _state = State.Main;
        private Lang _lang = Lang.ZH;
        private int _count = 10;       // 备用默认10秒
        private int _selectedMode = 0; // 选中模式：0=纯熄屏 1=锁屏+熄屏 2=延迟10秒后锁屏+熄屏，由 RadioButton/数字键/↑↓ 统一决定
        private int _delaySeconds = 10; // 模式2的延迟秒数，可由用户通过 NumericUpDown 自行输入，默认10秒
        private int _delayAction = 1;
        private bool _isDark = false;

        // ===== 控件 =====
        private System.Windows.Forms.Timer _timer;
        private Panel _topPanel;
        private Label _lblTitle;
        private Label _lblCountdown;
        private Label _lblCurrentMode;
        private Label _lblDefaultCaption;
        private RadioButton _radioMode0, _radioMode1, _radioMode2;
        private NumericUpDown _numDelaySeconds;
        private Label _lblDelaySuffix;
        private ComboBox _cmbMode2Action;
        private System.Windows.Forms.Timer _delayInputIdleTimer; // 秒数输入框：打字/按上下箭头后 1 秒空闲，自动判定输入完成
        private Label _lblOpt6, _lblOpt3, _lblEnterHint;
        private TableLayoutPanel _optionsPanel;
        private Button _btn0, _btn1, _btn2, _btn6, _btn3, _btnTheme, _btnLang;

        public MainForm()
        {
            //  优先从注册表读取上次保存的状态
            LoadSettings(); // 1. 先读取固化数据（此时 _selectedMode 拿到正确的值，比如 2）
            InitUI();       // 2. 初始化所有控件并绑定事件（此时单选框默认全为 false）
            ApplyTheme();
            RebuildTexts();

            // 3. 核心修复：在所有事件绑定完成后，再根据 _selectedMode 触发一次正确的勾选
            // 此时虽然也会触发 CheckedChanged，但它赋的值和 _selectedMode 原本的值是一样的，不会发生覆盖错误
            switch (_selectedMode)
            {
                case 0: _radioMode0.Checked = true; break;
                case 1: _radioMode1.Checked = true; break;
                case 2: _radioMode2.Checked = true; break;
            }

            StartMainCountdown();// 4. 最后开启倒计时
        }

        // 简易双语取词：中文/英文二选一，不使用外部资源文件
        private string L(string zh, string en) => _lang == Lang.ZH ? zh : en;

        private string ModeName(int mode)
        {
            switch (mode)
            {
                case 0: return L("纯熄屏", "Screen Off Only");
                case 1: return L("锁屏 + 熄屏", "Lock + Screen Off");
                case 2: return L($"延迟{_delaySeconds}秒后{ModeName(_delayAction)}", $"Delay {_delaySeconds}s then {ModeName(_delayAction)}");
                default: return "";
            }
        }


        private void InitUI()
        {
            // 核心修復 1：開啟窗體的自動調整大小，並限制它只能水平(橫向)自動變寬變窄，高度保持固定
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            //Text = "关闭屏幕或加锁定工具";
            ClientSize = new Size(460, 480);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true; // 让 Form 能优先捕获数字键，等价于 bat 的 choice 监听

            // ===== 顶部标题栏：标题 + 主题/语言按钮放进同一个 Panel，
            // 用 Dock 布局自动避让，不再使用绝对坐标（Location），
            // 从根本上解决不同分辨率/DPI/字体缩放下标题与按钮重叠的问题 =====
            _topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44
            };

            _lblTitle = new Label
            {
                Font = new Font("Microsoft YaHei", 14, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true
            };

            // ===== 修改后的主题按钮 =====
            _btnTheme = new Button
            {
                Text = "\U0001F313",
                Dock = DockStyle.Right,
                Width = 44, // 稍微调大一点点，和旁边的语言按钮保持视觉上的对齐
                FlatStyle = FlatStyle.Flat
            };
            _btnTheme.Click += (s, e) => { _isDark = !_isDark; ApplyTheme(); };
            
            // ===== 修改后的语言切换按钮 =====
            _btnLang = new Button
            {
                Dock = DockStyle.Right,
                AutoSize = true,              // 核心修复 1：开启自动大小，允许它根据 "EN" 的真实长度往左自适应延伸
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(44, 0), // 核心修复 2：设定最小宽度为 44，防止显示单字 "中" 时缩得太小难看
                TextAlign = ContentAlignment.MiddleCenter, // 核心修复 3：强行保证文字绝对居中
                FlatStyle = FlatStyle.Flat
            };


            _btnLang.Click += (s, e) => { _lang = _lang == Lang.ZH ? Lang.EN : Lang.ZH; RebuildTexts(); };
            // 添加顺序决定横向排列位置：同一 Dock 值下，后添加的控件更靠近对应边，
            // 所以先加标题(Fill)，再加语言按钮，最后加主题按钮，让主题按钮停在最右侧、语言按钮紧邻其左侧
            _topPanel.Controls.Add(_lblTitle);
            _topPanel.Controls.Add(_btnLang);
            _topPanel.Controls.Add(_btnTheme);

            _lblCountdown = new Label
            {
                Font = new Font("Microsoft YaHei", 12),
                Dock = DockStyle.Top,
                Height = 45,
                TextAlign = ContentAlignment.MiddleCenter
            };

            _lblCurrentMode = new Label
            {
                Font = new Font("Microsoft YaHei", 10),
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.MiddleCenter
            };

            // ===== 竖排选项列表（替代原来的一行横排提示） =====
            var _optionsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(24, 6, 10, 6)
            };
            _optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _lblDefaultCaption = new Label
            {
                Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 2)
            };

            _radioMode0 = new RadioButton
            {
                AutoSize = true,
                Checked = true,
                Font = new Font("Microsoft YaHei", 10),
                Margin = new Padding(0, 2, 0, 2)
            };
            _radioMode1 = new RadioButton
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei", 10),
                Margin = new Padding(0, 2, 0, 2)
            };
            _radioMode2 = new RadioButton
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei", 10),
                Margin = new Padding(0, 2, 4, 0) // 底部margin移交给下面的_mode2Row统一承担
            };

            //不依赖容器分组，手动强制互斥
            _radioMode0.CheckedChanged += (s, e) => { if (_radioMode0.Checked) { _selectedMode = 0; UpdateLabel(); } };
            _radioMode1.CheckedChanged += (s, e) => { if (_radioMode1.Checked) { _selectedMode = 1; UpdateLabel(); } };
            _radioMode2.CheckedChanged += (s, e) => { if (_radioMode2.Checked) { _selectedMode = 2; UpdateLabel(); } };

            // ===== 新增：模式2的延迟秒数输入框（用 NumericUpDown 代替裸TextBox，
            // 自带数字校验和上下微调箭头，不用自己写文本合法性校验逻辑）=====
            _numDelaySeconds = new NumericUpDown
            {
                Minimum = 1,    // 最小改为0时自动跑默认值10秒
                Maximum = 9999999,
                Value = Math.Max(1, Math.Min(9999999, _delaySeconds)),
                Width = 100,   //满足7位数
                Font = new Font("Microsoft YaHei", 10),
                TextAlign = HorizontalAlignment.Center,
                Margin = new Padding(0, 0, 2, 0)
            };
            _numDelaySeconds.ValueChanged += (s, e) =>
            {
                // 改为追踪用户输入的 _delaySeconds
                _delaySeconds = (int)_numDelaySeconds.Value == 0 ? 10 : (int)_numDelaySeconds.Value;   // 输入若最小值允许为0时，自动跑10秒默认值

                // 只要当前正在跑的倒计时本来就该用这个秒数，
                // 改完数值就立刻同步到 _count，倒计时马上跟着变，不需要再触发一次模式2/回车才生效
                // 不分选中哪个模式：只要主倒计时正在跑，或处于模式2手动触发的延迟锁屏阶段，改完秒数就立刻生效，
                // 这样跟 StartMainCountdown() 本来就"不分模式统一用这个秒数框的值"的逻辑保持一致
                if (_state == State.Main || _state == State.DelayLock)
                {
                    _count = _delaySeconds;
                }

                RebuildTexts(); // 秒数变化后，联动刷新选项文字/当前模式提示等所有引用到秒数的地方
                RestartDelayIdleTimer(); // 按上下箭头改数值，也算一次"编辑行为"，重新计1秒空闲
            };
            _numDelaySeconds.TextChanged += (s, e) => RestartDelayIdleTimer(); // 用户直接打字，每敲一个字符就重新计1秒空闲
            _numDelaySeconds.MouseLeave += (s, e) => CommitDelayInput(); // 鼠标离开输入框区域：不等1秒，立即判定输入完成

            //注释掉以下3行，改回系统默认常驻边框
            _numDelaySeconds.BorderStyle = BorderStyle.None; // 默认不显示边框，跟 ComboBox 的默认状态对齐
            _numDelaySeconds.MouseEnter += (s, e) => _numDelaySeconds.BorderStyle = BorderStyle.FixedSingle;
            _numDelaySeconds.MouseLeave += (s, e) => _numDelaySeconds.BorderStyle = BorderStyle.None;

            // 不设置 BorderStyle（保留控件默认值 Fixed3D 即可），或显式写成：
            //_numDelaySeconds.BorderStyle = BorderStyle.Fixed3D;

            _delayInputIdleTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _delayInputIdleTimer.Tick += (s, e) => CommitDelayInput();

            _lblDelaySuffix = new Label
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei", 10),
                Margin = new Padding(0, 4, 0, 0)
            };

            _cmbMode2Action = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, // 禁止手打文字，只能从列表选，避免出现无效值
                //FlatStyle = FlatStyle.Flat, // 关键：默认的 Standard 会用系统视觉样式绘制方框，直接忽略 BackColor/ForeColor
                FlatStyle = FlatStyle.Popup, // 同样能让 BackColor/ForeColor 生效，但比 Flat 多保留一圈边框，视觉上贴近输入框
                //FlatStyle = FlatStyle.Standard, // 用回系统默认渲染，跟 NumericUpDown 一样都是"原生边框"，风格类别统一
                Font = new Font("Microsoft YaHei", 10),
                Margin = new Padding(4, 2, 0, 0)
                // 不再写死 Width，改为在 RebuildTexts() 里根据当前语言的文字内容动态计算
            };
            _cmbMode2Action.SelectedIndexChanged += (s, e) =>
            {
                if (_cmbMode2Action.SelectedIndex >= 0)
                {
                    _delayAction = _cmbMode2Action.SelectedIndex; // 0=纯熄屏 1=锁屏+熄屏，与 ModeName(0)/(1) 对应
                    UpdateLabel(); // 只需刷新引用到延迟动作名称的文字，不需要整份 RebuildTexts
                }
            };


            _lblOpt6 = new Label { AutoSize = true, Font = new Font("Microsoft YaHei", 10), Margin = new Padding(20, 2, 0, 2) };
            _lblOpt3 = new Label { AutoSize = true, Font = new Font("Microsoft YaHei", 10), Margin = new Padding(20, 2, 0, 8) };
            _lblEnterHint = new Label { AutoSize = true, Font = new Font("Microsoft YaHei", 9, FontStyle.Bold), Margin = new Padding(0, 4, 0, 2) };

            int row = 0;
            _optionsPanel.Controls.Add(_lblDefaultCaption, 0, row); _optionsPanel.SetColumnSpan(_lblDefaultCaption, 4); row++;
            _optionsPanel.Controls.Add(_radioMode0, 0, row); _optionsPanel.SetColumnSpan(_radioMode0, 4); row++;
            _optionsPanel.Controls.Add(_radioMode1, 0, row); _optionsPanel.SetColumnSpan(_radioMode1, 4); row++;

            _optionsPanel.Controls.Add(_radioMode2, 0, row);
            _optionsPanel.Controls.Add(_numDelaySeconds, 1, row);
            _optionsPanel.Controls.Add(_lblDelaySuffix, 2, row);
            _optionsPanel.Controls.Add(_cmbMode2Action, 3, row);
            row++;

            _optionsPanel.Controls.Add(_lblOpt6, 0, row); _optionsPanel.SetColumnSpan(_lblOpt6, 4); row++;
            _optionsPanel.Controls.Add(_lblOpt3, 0, row); _optionsPanel.SetColumnSpan(_lblOpt3, 4); row++;
            _optionsPanel.Controls.Add(_lblEnterHint, 0, row); _optionsPanel.SetColumnSpan(_lblEnterHint, 4); row++;


            // ===== 底部操作按钮 =====

            // 核心修復 2：取消 DockLayout，改用 AutoSize 讓按鈕的寬度決定這個 panel 的總寬度
            _optionsPanel.Margin = new Padding(24, 6, 24, 0); // 讓中間面板也保留右側邊距

            var panel = new FlowLayoutPanel
            {
                Location = new Point(0, 410), // 給予一個初始底部的坐標
                Height = 60,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(24, 10, 24, 10), // 左右保留 24 像素的邊距，與上方對齊
                WrapContents = false, // 絕對不允許按鈕換行
                AutoSize = true,     // 核心：寬度由內部的按鈕總和決定
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            _btn0 = MakeButton((s, e) => OnKey0());
            _btn1 = MakeButton((s, e) => OnKey1());
            _btn2 = MakeButton((s, e) => OnKey2());
            _btn6 = MakeButton((s, e) => OnKey6());
            _btn3 = MakeButton((s, e) => OnKey3());

            panel.Controls.AddRange(new Control[] { _btn0, _btn1, _btn2, _btn6, _btn3 });

            Controls.Add(panel);
            Controls.Add(_optionsPanel);
            Controls.Add(_lblCurrentMode);
            Controls.Add(_lblCountdown);
            Controls.Add(_topPanel);

            KeyDown += MainForm_KeyDown;

            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += Timer_Tick;
        }
                
        private Button MakeButton(EventHandler onClick)
        {
            // 核心修復 3：利用 WinForms 原生的 AutoSize 根據中英文長度自動決定按鈕寬度
            var b = new Button
            {
                Height = 40,
                AutoSize = true,
                Padding = new Padding(6, 0, 6, 0), // 讓文字左右留有安全的呼吸空間，視覺更美觀
                Margin = new Padding(4, 0, 4, 0)   // 按鈕與按鈕之間的左右間距
            };
            b.Click += onClick;
            return b;
        }

        // ===== 秒数输入框的"自动固化/自动失焦"逻辑 =====

        // 返回当前 _selectedMode 对应的 RadioButton。
        // 注意：不能无条件 Focus() 到 _radioMode2，因为 WinForms 的 RadioButton 有个特性——
        // AutoCheck=true 时，只要控件通过 Focus() 获得焦点就会自动变成 Checked=true。
        // 如果用户是在选中模式0/1的情况下顺手改了秒数，此时若强行把焦点丢给 _radioMode2，
        // 会把选中模式"意外"篡改成2。所以要焦点回到"当前本来就选中的那个"，这是无副作用的。
        private RadioButton CurrentRadio() => _selectedMode switch
        {
            0 => _radioMode0,
            1 => _radioMode1,
            _ => _radioMode2
        };

        // 重新开始计时：只要用户还在跟输入框交互（打字/点上下箭头），就不断把1秒倒计时往后推
        private void RestartDelayIdleTimer()
        {
            _delayInputIdleTimer.Stop();
            _delayInputIdleTimer.Start();
        }

        // 固化输入：把焦点从秒数输入框挪走。
        // NumericUpDown 失去焦点时会自带一次校验(Validating)，自动把编辑框里的文字解析并夹紧进 Value，
        // 所以这里不需要手动解析文本——只要让它失焦，"固化"这个动作就自然完成了。
        private void CommitDelayInput()
        {
            _delayInputIdleTimer.Stop();
            if (_numDelaySeconds.Focused)
            {
                CurrentRadio().Focus();
            }
        }

        // ===== 语言/文案：所有需要翻译的控件文字集中在这里刷新 =====
        private void RebuildTexts()
        {
            Text = L("关闭屏幕或加锁定工具", "Screen Off / Lock Tool");
            _lblTitle.Text = L("关闭屏幕或加锁定 2026", "Screen Off / Lock 2026");

            _lblDefaultCaption.Text = L("选中模式（超时或回车触发）：", "Selected Mode (on timeout / Enter):");
            _radioMode0.Text = "0 - " + L("纯熄屏", "Screen Off Only");
            _radioMode1.Text = "1 - " + L("锁屏 + 熄屏", "Lock + Screen Off");
            _radioMode2.Text = "2 - " + L("延迟", "Delay");
            _lblDelaySuffix.Text = L("秒后", "s, then");

            _cmbMode2Action.Items.Clear();
            _cmbMode2Action.Items.Add(ModeName(0));
            _cmbMode2Action.Items.Add(ModeName(1));
            _cmbMode2Action.SelectedIndex = _delayAction;

            // 按当前语言下最长的一项文字实际宽度，动态设定下拉框宽度（原理上类似按钮的 AutoSize，
            // 但 ComboBox 本身不支持按内容自动变宽，所以手动量字体宽度来模拟同样的效果）
            using (Graphics g = _cmbMode2Action.CreateGraphics())
            {
                float maxTextWidth = 0;
                foreach (var item in _cmbMode2Action.Items)
                {
                    float w = g.MeasureString(item.ToString(), _cmbMode2Action.Font).Width;
                    if (w > maxTextWidth) maxTextWidth = w;
                }
                // +40 是下拉箭头按钮 + 左右内边距的经验预留值，避免文字紧贴边框或被箭头挡住
                _cmbMode2Action.Width = (int)maxTextWidth + 40;
            }

            _lblOpt6.Text = "6 - " + L("暂停倒计时", "Pause Countdown");
            _lblOpt3.Text = "3 - " + L("退出脚本程序", "Exit Script");
            _lblEnterHint.Text = "\u23CE " + L("回车 = 执行选中模式", "Enter = Run Selected Mode")
    + "\n" + L("←/→ 选中要执行的模式，↑/↓ 切换选中模式", "←/→ select action, ↑/↓ change selected mode");

            _btn0.Text = "0 " + L("熄屏", "Off");
            _btn1.Text = "1 " + L("锁屏+熄屏", "Lock+Off");
            _btn2.Text = "2 " + L("延迟模式", "Delay Mode");
            _btn6.Text = "6 " + L("暂停", "Pause");
            _btn3.Text = "3 " + L("退出", "Exit");

            _btnLang.Text = _lang == Lang.ZH ? "EN" : "\u4E2D";

            UpdateLabel();

            // 核心修復 4：當所有按鈕文字變更後，強迫窗體在下一幀重新計算並適應最寬的控制項（即按鈕面板）
            Application.DoEvents();
        }


        // ===== 主题：最简单方式，只切换前景/背景色，不建立单独主题类 =====
        private void ApplyTheme()
        {
            Color bg = _isDark ? Color.FromArgb(30, 30, 30) : Color.White;
            Color fg = _isDark ? Color.White : Color.Black;
            Color btnBg = _isDark ? Color.FromArgb(50, 50, 50) : Color.FromArgb(240, 240, 240);

            // 專門為 Flat 邊框準備的顏色：黑暗模式下用亮灰邊框，亮色模式下用深灰邊框
            Color borderColor = _isDark ? Color.FromArgb(100, 100, 100) : Color.FromArgb(180, 180, 180);

            BackColor = bg;
            foreach (Control c in AllControls(this))
            {
                if (c is Label || c is RadioButton)
                {
                    c.ForeColor = fg;
                    c.BackColor = bg;
                }
                // 核心修復：取消 btn != _btnTheme 的限制，讓主題按鈕完美參與變色
                else if (c is Button btn)
                {
                    btn.ForeColor = fg;
                    btn.BackColor = btnBg;
                    btn.FlatStyle = FlatStyle.Flat;

                    // 額外優化：顯式設定 Flat 外觀的邊框顏色，確保黑夜模式下邊緣完美取反
                    btn.FlatAppearance.BorderColor = borderColor;
                }
                else if (c is Panel)
                {
                    c.BackColor = bg;
                }
                else if (c is NumericUpDown nud)
                {
                    nud.ForeColor = fg;
                    nud.BackColor = _isDark ? Color.FromArgb(50, 50, 50) : Color.White;
                }
                else if (c is ComboBox cmb)
                {
                    cmb.ForeColor = fg;
                    cmb.BackColor = _isDark ? Color.FromArgb(50, 50, 50) : Color.White;
                }
            }
        }

        private IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var sub in AllControls(c)) yield return sub;
            }
        }

        // ===== 主倒计时循环（对应 bat 的 :TIMER_LOOP）=====
        private void StartMainCountdown()
        {
            _state = State.Main;
            _count = (int)_numDelaySeconds.Value == 0 ? 10 : (int)_numDelaySeconds.Value;   // 输入若最小值允许为0时，自动跑10秒默认值
            UpdateLabel();
            _timer.Start();
        }

        private void UpdateLabel()
        {
            // 三种状态统一显示同一个"选中模式"，不再有"生效模式"/"默认模式"两套说法
            _lblCurrentMode.Text = L($"选中模式：{ModeName(_selectedMode)}", $"Selected mode: {ModeName(_selectedMode)}");

            switch (_state)
            {
                case State.Main:
                    _lblCountdown.Text = L($"{_count} 秒后自动执行...", $"Auto-run in {_count}s...");
                    break;
                case State.DelayLock:
                    _lblCountdown.Text = L($"{_count} 秒后自动执行...", $"Auto-run in {_count}s...");
                    break;
                case State.Paused:
                    _lblCountdown.Text = L("已暂停，按任意键继续...", "Paused — press any key to continue...");
                    break;
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_state == State.Paused) return;

            _count--;
            if (_count <= 0)
            {
                _timer.Stop();
                if (_state == State.DelayLock)
                {
                    Execute(mode: _delayAction); // 手动触发的第二段延迟倒计时到点，执行
                }
                else if (_state == State.Main && _selectedMode == 2)
                {
                    // 主倒计时在选中模式2时，本身就已经在同步显示 _delaySeconds（见秒数框 ValueChanged 里的同步逻辑），
                    // 所以这里到点就代表延迟已经走完了，必须直接执行；不能再调 RunSelectedMode()/OnKey2()
                    // 重新从 _delaySeconds 倒数一遍，否则就是你看到的"倒数完不执行、又倒数一次才执行"
                    Execute(_delayAction);
                }
                else
                {
                    RunSelectedMode(); // 主倒计时到点：0/1 立即执行
                }
                return;
            }
            UpdateLabel();
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            // 秒数输入框正在编辑：所有全局快捷键统一让路，避免输入数字被当成全局指令劫持（比如按3会被当成"退出"）
            if (_numDelaySeconds != null && _numDelaySeconds.Focused)
            {
                return;
            }
            // 核心修复：如果按的是 3，不论当前是暂停还是倒数，都必须立刻执行 3 的核心逻辑！
            if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3)
            {
                OnKey3();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            // 处于暂停状态时的其他按键逻辑
            if (_state == State.Paused)
            {
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    return; // 暂停状态下允许方向键正常切换默认模式，不退出暂停
                }

                if (e.KeyCode == Keys.Enter)
                {
                    // 回车 = 执行当前选中模式，效果与直接按对应数字键完全一致
                    RunSelectedMode();
                }
                else
                {
                    // 此时真正的“任意键”（不包含3了）才会恢复倒计时
                    // 其他任意键：保持原来的"按任意键继续"行为
                    _state = State.Main;
                    _count = (int)_numDelaySeconds.Value;   //_count = 10;
                    UpdateLabel();
                    _timer.Start();
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            // 正常倒数状态下的其他数字键逻辑
            switch (e.KeyCode)
            {
                case Keys.D0:
                case Keys.NumPad0: OnKey0(); break;
                case Keys.D1:
                case Keys.NumPad1: OnKey1(); break;
                case Keys.D2:
                case Keys.NumPad2: OnKey2(); break;
                case Keys.D6:
                case Keys.NumPad6: OnKey6(); break;

                // 回车 = 执行选中模式（与按对应数字键效果一致）
                case Keys.Enter: RunSelectedMode();break;
                case Keys.T: _isDark = !_isDark; ApplyTheme(); break;
                case Keys.L: _lang = _lang == Lang.ZH ? Lang.EN : Lang.ZH; RebuildTexts(); break;
                default: return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        // 0 = 立即纯熄屏
        private void OnKey0()
        {
            _timer.Stop();
            Execute(0);
        }

        // 1 = 立即锁屏+熄屏
        private void OnKey1()
        {
            _timer.Stop();
            Execute(1);
        }

        // 2 = 进入"延迟自定义秒后锁屏+熄屏"模式（对应 bat 的 :DELAY_LOCK_LOOP）
        private void OnKey2()
        {
            _state = State.DelayLock;
            _count = _delaySeconds;
            UpdateLabel();
            if (!_timer.Enabled) _timer.Start();
        }

        // 3 = 直接退出脚本
        // 核心修复：取消“中止回主菜单”的设定，只要按 3 就直接彻底退出程序
        private void OnKey3()
        {
            // 退出点 1：修改 OnKey3 方法
            SaveSettings(); // 👈 退出前固化数据
            Environment.Exit(0);
        }

        // 6 = 暂停倒计时
        private void OnKey6()
        {
            _state = State.Paused;
            _timer.Stop();
            UpdateLabel();
        }

        // 统一入口：按当前"选中模式"触发对应动作，回车键与直接按数字键 0/1/2 效果完全一致
        private void RunSelectedMode()
        {
            switch (_selectedMode)
            {
                case 0: OnKey0(); break;
                case 1: OnKey1(); break;
                case 2: OnKey2(); break;
            }
        }

        // ===== 对应 bat 的 :APPLY_SETTINGS，并满足"执行完毕后自退出"需求 =====
        private void Execute(int mode)
        {
            _timer.Stop();

            // 先隐藏窗口，避免后续关闭/销毁窗口产生的激活消息把刚熄灭的屏幕又唤醒
            this.Hide();
            Application.DoEvents();

            if (mode == 1 || mode == 2) // 模式2本质上就是模式1（锁屏+熄屏），只是通过倒计时刷新来延迟触发
            {
                LockWorkStation();
                System.Threading.Thread.Sleep(1000); // 对应 bat 的 timeout /t 1
            }
            // 对应 bat 的 powershell SendMessage 熄屏指令
            // 注意：原 bat 传入 hWnd=-1（非法句柄，实际可能无效果或依赖系统巧合）
            // 这里改用当前窗口句柄 this.Handle，是更稳妥、明确有效的写法
            SendMessage(this.Handle, WM_SYSCOMMAND, SC_MONITORPOWER, 2);

            // 用 Environment.Exit 而非 Application.Exit：
            // Application.Exit 只是停止消息循环、逐步关闭窗体，进程可能延迟才真正结束；
            // Environment.Exit 立刻终止整个进程，确保"运行完毕即彻底退出，无残留"。

            // 退出点 2：修改 Execute 方法的最末尾
            SaveSettings(); // 👈 熄屏彻底退出前固化数据
            Environment.Exit(0);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // 1. 拦截方向键：切换选中模式
            // 但如果焦点正在秒数输入框里，则放行给 NumericUpDown 自己处理上下调整数值，不抢它的按键
            if ((keyData == Keys.Up || keyData == Keys.Down) && _numDelaySeconds != null && _numDelaySeconds.Focused)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }
            if (keyData == Keys.Up || keyData == Keys.Down)
            {
                CycleSelectedMode(keyData == Keys.Down);
                _state = State.Paused;
                _timer.Stop();
                UpdateLabel();
                // 完全消费此事件
                return true; // 自己处理完就返回 true，不再往下传，
                             // 避免被系统内置的单选框方向键导航逻辑抢先拦截而"没反应"
            }

            // 2. 核心修复：强行拦截回车键，确保触发当前选中的模式逻辑
            if (keyData == Keys.Enter)
            {
                RunSelectedMode();
                return true; // 强行截断 WinForms 自带的焦点激活逻辑，防止误触其他按钮
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // 在 0/1/2 之间循环切换选中模式（forward=true 往下切，false 往上切）
        private void CycleSelectedMode(bool forward)
        {
            int next = _selectedMode + (forward ? 1 : -1);
            if (next > 2) next = 0;
            if (next < 0) next = 2;

            switch (next)
            {
                case 0: _radioMode0.Checked = true; break;
                case 1: _radioMode1.Checked = true; break;
                case 2: _radioMode2.Checked = true; break;
            }
            // 注意：把 Checked 设为 true 会自动触发对应 RadioButton 的 CheckedChanged 事件，
            // 里面已经会更新 _selectedMode 并调用 UpdateLabel()，这里不需要再手动赋值 _selectedMode
        }

        // 注册表保存路径
        private const string RegPath = @"Software\VirtualScreenSwitch\Settings";

        private void LoadSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (key != null)
                    {
                        _selectedMode = (int)key.GetValue("SelectedMode", 0);
                        _lang = (Lang)(int)key.GetValue("Lang", (int)Lang.ZH);
                        _isDark = Convert.ToBoolean(key.GetValue("IsDark", 0));
                        _delaySeconds = (int)key.GetValue("DelaySeconds", 10);
                        _delayAction = (int)key.GetValue("DelayAction", 1); // 默认1=锁屏+熄屏，与控件默认值保持一致
                    }
                }
            }
            catch { /* 忽略异常，防读取失败 */ }
        }

        private void SaveSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    if (key != null)
                    {
                        key.SetValue("SelectedMode", _selectedMode);
                        key.SetValue("Lang", (int)_lang);
                        key.SetValue("IsDark", _isDark ? 1 : 0);
                        key.SetValue("DelaySeconds", _delaySeconds);
                        key.SetValue("DelayAction", _delayAction);
                    }
                }
            }
            catch { /* 忽略异常，防无写入权限 */ }
        }
    }
}

// 备注：由于是单文件轻量应用，因此各种function函数没有作拆分，后续有更多需求再考虑拆分成多个类文件。
