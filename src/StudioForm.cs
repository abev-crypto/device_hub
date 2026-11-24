using System.ComponentModel;

namespace G600Studio;

internal sealed class StudioForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(20, 24, 31);
    private static readonly Color Surface = Color.FromArgb(30, 36, 46);
    private static readonly Color ControlColor = Color.FromArgb(42, 51, 65);
    private static readonly Color Ink = Color.FromArgb(241, 246, 252);
    private static readonly Color Muted = Color.FromArgb(177, 192, 208);
    private static readonly Color Accent = Color.FromArgb(107, 225, 197);
    private static readonly Color AccentBackground = Color.FromArgb(31, 71, 71);
    private static readonly Color Alert = Color.FromArgb(255, 181, 161);

    private readonly Label connection = LabelFor("未接続", 10, Muted);
    private readonly Label title = LabelFor("ボタン割り当て", 24, Ink, true);
    private readonly Label status = LabelFor("G600 の設定を読み込んでいます…", 9, Muted);
    private readonly Button reload = ButtonFor("再読み込み", 140);
    private readonly Button restore = ButtonFor("バックアップを開く", 164);
    private readonly Button startup = ButtonFor("スタートアップに登録", 178);
    private readonly Button useMode = ButtonFor("このモードを使用", 150);
    private readonly Button save = ButtonFor("本体に保存", 142, true);
    private readonly Button[] modeButtons = [ButtonFor("モード 1", 178), ButtonFor("モード 2", 178), ButtonFor("モード 3", 178)];
    private readonly Button[] pageButtons = [ButtonFor("ボタン", 106), ButtonFor("DPI・応答速度", 135), ButtonFor("ライティング", 125)];
    private readonly Panel body = new();
    private readonly FlowLayoutPanel disconnected = OpaqueSurface(Row(), Canvas);
    private readonly Page?[] pages = new Page[3];
    private readonly TableLayoutPanel layout = new();

    private Profile[] original = [];
    private Profile[] edited = [];
    private string devicePath = "";
    private int activeMode = -1;
    private int activeDpi;
    private int mode;
    private int page;
    private int selectedButton = 9;
    private bool shifted;
    private bool busy;
    private bool startupBusy;
    private bool connected;
    private bool refreshingPage;
    private Control? displayed;

    public StudioForm()
    {
        Text = "G600 Studio";
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        Font = new Font("Yu Gothic UI", 10);
        BackColor = Canvas;
        ForeColor = Ink;
        MinimumSize = new Size(915, 720);
        ClientSize = new Size(1030, 730);
        StartPosition = FormStartPosition.CenterScreen;

        layout.Dock = DockStyle.Fill;
        layout.ColumnCount = 2;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowCount = 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var sidebar = OpaqueSurface(Stack(new Padding(19, 24, 12, 12)), Surface);
        sidebar.Dock = DockStyle.Fill;
        sidebar.Margin = Padding.Empty;
        sidebar.Controls.Add(LabelFor("G600 Studio", 20, Ink, true));
        sidebar.Controls.Add(connection);
        Space(sidebar, 30);
        sidebar.Controls.Add(LabelFor("本体メモリ", 10, Muted, true));
        foreach (var (button, index) in modeButtons.Select((button, index) => (button, index)))
        {
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(8, 0, 0, 0);
            button.Click += (_, _) => { mode = index; RenderPage(); };
            sidebar.Controls.Add(button);
        }
        Space(sidebar, 22);
        sidebar.Controls.Add(reload);
        sidebar.Controls.Add(restore);
        Space(sidebar, 16);
        sidebar.Controls.Add(startup);
        layout.Controls.Add(sidebar, 0, 0);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 19, 24, 10), Margin = Padding.Empty, RowCount = 3, ColumnCount = 1 };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 107));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        var header = OpaqueSurface(new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(10, 5, 10, 0), RowCount = 2, ColumnCount = 1
        }, Canvas);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(title, 0, 0);
        var navigation = Row();
        foreach (var (button, index) in pageButtons.Select((button, index) => (button, index)))
        {
            button.Click += (_, _) => { page = index; RenderPage(); };
            navigation.Controls.Add(button);
        }
        header.Controls.Add(navigation, 0, 1);
        main.Controls.Add(header, 0, 0);

        body.Dock = DockStyle.Fill;
        body.Margin = Padding.Empty;
        disconnected.Controls.Add(LabelFor("Logicool G600 を USB で接続してください。", 11, Muted));
        body.Controls.Add(disconnected);
        main.Controls.Add(body, 0, 1);

        var footer = OpaqueSurface(new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(10, 9, 10, 0),
            Margin = new Padding(0, 5, 0, 0), RowCount = 2, ColumnCount = 1
        }, Canvas);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = Row();
        actions.Controls.Add(save);
        actions.Controls.Add(useMode);
        footer.Controls.Add(actions, 0, 0);
        status.MaximumSize = new Size(690, 0);
        footer.Controls.Add(status, 0, 1);
        main.Controls.Add(footer, 0, 2);
        layout.Controls.Add(main, 1, 0);

        Shown += async (_, _) => await Reload(false);
        reload.Click += async (_, _) => await Reload(true);
        save.Click += async (_, _) => await Save();
        useMode.Click += async (_, _) => await ActivateMode();
        restore.Click += (_, _) => LoadBackup();
        startup.Click += async (_, _) => await ToggleStartup();
        FormClosing += (_, args) => args.Cancel = args.CloseReason != CloseReason.WindowsShutDown &&
            (busy || startupBusy || !ConfirmDiscard("終了しますか？"));
        RefreshStartup();
        RenderPage();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateBackdrop();
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        const int settingsChanged = 0x001a;
        const int compositionChanged = 0x031e;
        if (IsHandleCreated && message.Msg is settingsChanged or compositionChanged)
            UpdateBackdrop();
    }

    private void UpdateBackdrop()
    {
        var acrylic = WindowBackdrop.Apply(Handle, !SystemInformation.HighContrast);
        // DWM uses zero-valued pixels of the extended GDI frame to expose its backdrop.
        BackColor = acrylic ? Color.Black : Canvas;
    }

    private Profile Current => edited[mode];
    private bool Dirty(int index) => !original[index].Bytes.SequenceEqual(edited[index].Bytes);

    private void RefreshStartup()
    {
        try
        {
            startup.Text = StartupRegistration.IsRegistered() ? "スタートアップ登録を解除" : "スタートアップに登録";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            startup.Enabled = false;
            SetStatus($"スタートアップ登録を確認できませんでした。{error.Message}", true);
        }
    }

    private async Task ToggleStartup()
    {
        startupBusy = true;
        startup.Enabled = false;
        try
        {
            var registered = await Task.Run(() =>
            {
                var register = !StartupRegistration.IsRegistered();
                StartupRegistration.SetRegistered(register);
                return register;
            });
            SetStatus(registered ? "登録しました。次回のサインイン時に通知領域で起動します。" : "スタートアップ登録を解除しました。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            SetStatus($"スタートアップを変更できませんでした。{error.Message}", true);
        }
        finally
        {
            startupBusy = false;
            if (!IsDisposed)
            {
                startup.Enabled = true;
                RefreshStartup();
            }
        }
    }

    private bool ConfirmDiscard(string message)
    {
        return edited.Length == 0 || !Enumerable.Range(0, edited.Length).Any(Dirty) ||
            MessageBox.Show(this, $"未保存の変更を破棄して{message}", Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private async Task Reload(bool confirm)
    {
        if (confirm && !ConfirmDiscard("再読み込みしますか？"))
            return;
        await Run("G600 の設定を読み込み中…", () =>
        {
            using var device = G600Device.Open();
            var active = device.ReadActive();
            var profiles = Enumerable.Range(0, G600Device.ProfileCount)
                .Select(index => new Profile(device.ReadProfile(index))).ToArray();
            return (device.Path, active.Profile, active.DpiSlot, profiles);
        }, result =>
        {
            devicePath = result.Path;
            activeMode = result.Profile;
            activeDpi = result.DpiSlot;
            original = result.profiles;
            edited = original.Select(profile => profile.Copy()).ToArray();
            mode = activeMode is >= 0 and < G600Device.ProfileCount ? activeMode : 0;
            connected = true;
            RenderPage();
            SetStatus("本体メモリの 3 モードを読み込みました。");
        }, disconnectOnError: true);
    }

    private async Task Save()
    {
        var index = mode;
        var baseline = original[index];
        var desired = Current.Copy();
        if (!Dirty(index))
            return;

        if (baseline.Bytes.AsSpan(13, 5).SequenceEqual(desired.Bytes.AsSpan(13, 5)) is false &&
            (desired.DefaultSlot is < 1 or > 4 || desired.Dpi(desired.DefaultSlot - 1) == 0))
        {
            SetStatus("開始 DPI には、数値が設定されているスロットを選んでください。", true);
            return;
        }

        await Run($"モード {index + 1} を保存して読み戻し中…", () =>
        {
            using var device = OpenSameDevice();
            var actual = new Profile(device.ReadProfile(index));
            if (!actual.Bytes.SequenceEqual(baseline.Bytes))
                throw new IOException("本体の設定が読み込み後に変わりました。再読み込みしてください。");

            var backup = BackupFile.Save(index, actual);
            try
            {
                device.WriteProfile(index, desired.Bytes);
                if (!device.ReadProfile(index).SequenceEqual(desired.Bytes))
                    throw new IOException("本体から読み戻した設定が書き込み内容と一致しません。");
            }
            catch (Exception error) when (error is IOException or Win32Exception)
            {
                try
                {
                    if (!device.ReadProfile(index).SequenceEqual(actual.Bytes))
                        device.WriteProfile(index, actual.Bytes);
                    if (!device.ReadProfile(index).SequenceEqual(actual.Bytes))
                        throw new IOException("元の設定と一致しません。");
                }
                catch (Exception recoveryError) when (recoveryError is IOException or Win32Exception)
                {
                    throw new IOException($"保存後の確認と自動復元に失敗しました。再接続し、バックアップ「{Path.GetFileName(backup)}」を開いてください。", recoveryError);
                }
                throw new IOException($"{error.Message} 元の設定に戻しました。", error);
            }
            return desired;
        }, saved =>
        {
            original[index] = saved;
            edited[index] = saved.Copy();
            SetStatus($"モード {index + 1} を保存し、読み戻しを確認しました。使用するには「このモードを使用」を押してください。");
            RenderPage();
        });
    }

    private async Task ActivateMode()
    {
        var index = mode;
        await Run($"モード {index + 1} に切り替え中…", () =>
        {
            using var device = OpenSameDevice();
            device.SelectProfile(index);
            Thread.Sleep(200);
            var selection = device.ReadActive();
            if (selection.Profile != index)
            {
                device.SelectProfile(index);
                Thread.Sleep(200);
                selection = device.ReadActive();
            }
            if (selection.Profile != index)
                throw new IOException("モードの切り替えを確認できませんでした。");
            return selection;
        }, selection =>
        {
            activeMode = selection.Profile;
            activeDpi = selection.DpiSlot;
            SetStatus($"モード {index + 1} を使用中です。");
            RenderPage();
        });
    }

    private async Task ActivateDpi(int index, int slot)
    {
        await Run($"DPI スロット {slot + 1} に切り替え中…", () =>
        {
            using var device = OpenSameDevice();
            var selection = device.ReadActive();
            var stored = new Profile(device.ReadProfile(index));
            if (selection.Profile != index || !stored.Bytes.SequenceEqual(original[index].Bytes))
                throw new IOException("使用中のモードまたは保存済み設定が変わりました。再読み込みしてください。");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                device.SelectDpi(slot);
                Thread.Sleep(200);
                selection = device.ReadActive();
                if (selection.Profile == index && selection.DpiSlot == slot)
                    return stored.Dpi(slot);
                if (selection.Profile != index)
                    break;
            }
            throw new IOException("DPI の切り替えを確認できませんでした。");
        }, dpi =>
        {
            activeDpi = slot;
            SetStatus($"{dpi} DPI を使用中です。");
            RenderPage();
        });
    }

    private G600Device OpenSameDevice()
    {
        var device = G600Device.Open();
        if (device.Path.Equals(devicePath, StringComparison.OrdinalIgnoreCase))
            return device;
        device.Dispose();
        throw new IOException("接続された G600 が変わりました。再読み込みしてください。");
    }

    private void LoadBackup()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "G600 Studio バックアップ (*.json)|*.json",
            InitialDirectory = BackupFile.DirectoryPath,
            Title = $"モード {mode + 1} のバックアップ"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            edited[mode] = BackupFile.Read(dialog.FileName, mode);
            SetStatus("バックアップを読み込みました。「本体に保存」で反映します。");
            RenderPage();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            SetStatus(error.Message, true);
        }
    }

    private async Task Run<T>(string message, Func<T> operation, Action<T> complete, bool disconnectOnError = false)
    {
        busy = true;
        SetStatus(message);
        RefreshControls();
        try
        {
            complete(await Task.Run(operation));
        }
        catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException)
        {
            if (disconnectOnError)
            {
                connected = false;
                RenderPage();
            }
            SetStatus(error.Message, true);
        }
        finally
        {
            busy = false;
            RefreshControls();
        }
    }

    private void SetStatus(string text, bool error = false)
    {
        status.Text = text;
        status.ForeColor = error ? Alert : Muted;
    }

    private void RefreshControls()
    {
        var ready = connected && !busy;
        reload.Enabled = !busy;
        restore.Enabled = ready;
        body.Enabled = ready;
        save.Enabled = ready && Dirty(mode);
        useMode.Enabled = ready && !Dirty(mode);
        connection.Text = connected ? "●  USB 接続済み" : "○  未接続";
        connection.ForeColor = connected ? Accent : Muted;
        for (var index = 0; index < modeButtons.Length; index++)
        {
            var button = modeButtons[index];
            button.Enabled = ready;
            var indicator = connected && index == activeMode ? "  使用中" : "";
            var change = connected && Dirty(index) ? "  *" : "";
            button.Text = $"モード {index + 1}{indicator}{change}";
            Highlight(button, index == mode);
        }
        for (var index = 0; index < pageButtons.Length; index++)
        {
            pageButtons[index].Enabled = ready;
            Highlight(pageButtons[index], index == page);
        }
    }

    private void RenderPage()
    {
        title.Text = page switch { 1 => "DPI・応答速度", 2 => "ライティング", _ => "ボタン割り当て" };
        Control next = disconnected;
        if (connected)
        {
            var current = pages[page] ??= CreatePage(page);
            next = current.View;
            refreshingPage = true;
            current.View.SuspendLayout();
            try { current.Refresh(); }
            finally
            {
                current.View.ResumeLayout();
                refreshingPage = false;
            }
        }
        if (displayed != next)
        {
            if (displayed is not null)
                displayed.Visible = false;
            next.Visible = true;
            displayed = next;
        }
        RefreshControls();
    }

    private sealed record Page(FlowLayoutPanel View, Action Refresh);

    private Page CreatePage(int index)
    {
        var pane = new BufferedPage
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Margin = Padding.Empty, Visible = false
        };
        pane.SuspendLayout();
        var refresh = index switch
        {
            1 => BuildDpiPage(pane),
            2 => BuildLightingPage(pane),
            _ => BuildButtonsPage(pane)
        };
        body.Controls.Add(pane);
        pane.ResumeLayout();
        return new Page(pane, refresh);
    }

    private sealed class BufferedPage : FlowLayoutPanel
    {
        public BufferedPage() => DoubleBuffered = true;
    }

    private Action BuildButtonsPage(FlowLayoutPanel pane)
    {
        var layers = OpaqueSurface(Row(), Canvas);
        var normal = ButtonFor("通常", 100);
        var shift = ButtonFor("G シフト", 110);
        layers.Controls.AddRange([normal, shift]);
        pane.Controls.Add(layers);
        Space(pane, 8);
        var columns = Row();
        var map = OpaqueSurface(Stack(new Padding(13)), Surface);
        map.Margin = new Padding(0, 0, 13, 0);
        var editor = OpaqueSurface(Stack(new Padding(13)), Surface);
        editor.MinimumSize = new Size(244, 328);
        columns.Controls.AddRange([map, editor]);
        pane.Controls.Add(columns);

        var buttonTiles = new Dictionary<int, Button>();
        PanelGrid(map, "サイドボタン", 9, 20, 3, buttonTiles);
        PanelGrid(map, "上面・ホイール", 1, 8, 4, buttonTiles);
        void RefreshTile(int number)
        {
            var tile = buttonTiles[number];
            var caption = Current.Button(number, shifted).Caption;
            tile.Text = $"G{number}\n{caption}";
            tile.AccessibleName = $"G{number} {Bindings.ButtonName(number)} {caption}";
            Highlight(tile, number == selectedButton);
        }
        var refreshEditor = BuildBindingEditor(editor, () => RefreshTile(selectedButton));
        void Select(int number)
        {
            if (number == selectedButton)
                return;
            Highlight(buttonTiles[selectedButton], false);
            selectedButton = number;
            Highlight(buttonTiles[number], true);
            refreshEditor();
        }
        foreach (var (number, tile) in buttonTiles)
            tile.Click += (_, _) => Select(number);
        normal.Click += (_, _) => { if (shifted) { shifted = false; RenderPage(); } };
        shift.Click += (_, _) => { if (!shifted) { shifted = true; RenderPage(); } };
        return () =>
        {
            Highlight(normal, !shifted);
            Highlight(shift, shifted);
            foreach (var number in buttonTiles.Keys)
                RefreshTile(number);
            refreshEditor();
        };
    }

    private static void PanelGrid(FlowLayoutPanel map, string name, int first, int last, int columns, Dictionary<int, Button> buttonTiles)
    {
        map.Controls.Add(LabelFor(name, 10, Muted, true));
        for (var number = first; number <= last;)
        {
            var row = Row();
            for (var col = 0; col < columns && number <= last; col++, number++)
            {
                var tile = ButtonFor("", columns == 3 ? 104 : 76);
                tile.Height = columns == 3 ? 44 : 40;
                tile.Font = new Font("Yu Gothic UI", 8.5f);
                tile.Margin = new Padding(0, 0, 4, 4);
                row.Controls.Add(tile);
                buttonTiles.Add(number, tile);
            }
            map.Controls.Add(row);
        }
        Space(map, 5);
    }

    private Action BuildBindingEditor(FlowLayoutPanel editor, Action onChange)
    {
        Binding initial = default;
        var updating = false;
        var heading = LabelFor("", 23, Ink, true);
        var name = LabelFor("", 9, Muted);
        editor.Controls.Add(heading);
        editor.Controls.Add(name);
        Space(editor, 10);
        var actions = new List<Choice> { new(-1, "割り当てなし"), new(0, "キー / ショートカット") };
        actions.AddRange(Bindings.Actions.Select(item => new Choice(item.Key, item.Value)));
        var action = ChoiceFor("ボタンの動作", 206, actions);
        var key = ChoiceFor("送信するキー", 206, Bindings.Keys.Select(item => new Choice(item.Key, item.Value)));
        editor.Controls.Add(action);
        editor.Controls.Add(key);
        var checks = new CheckBox[8];
        for (var start = 0; start < 8; start += 2)
        {
            var row = Row();
            for (var bit = start; bit < start + 2; bit++)
            {
                checks[bit] = new CheckBox
                {
                    ForeColor = Ink, Text = Bindings.ModifierNames[bit], Width = 95, Height = 23,
                    FlatStyle = FlatStyle.Flat, UseCompatibleTextRendering = true,
                    Margin = new Padding(0, 0, 5, 0)
                };
                row.Controls.Add(checks[bit]);
            }
            editor.Controls.Add(row);
        }
        void Change()
        {
            if (updating || action.SelectedItem is not Choice || key.SelectedItem is not Choice)
                return;
            var type = Selected(action);
            var keystroke = type == 0;
            key.Enabled = keystroke;
            byte mask = 0;
            for (var bit = 0; bit < checks.Length; bit++)
            {
                checks[bit].Enabled = keystroke;
                if (checks[bit].Checked)
                    mask |= (byte)(1 << bit);
            }
            var binding = type switch
            {
                -1 => default,
                0 => new Binding(0, mask, (byte)Selected(key)),
                _ when type == initial.Action => initial,
                _ => new Binding((byte)type, 0, 0)
            };
            Current.SetButton(selectedButton, shifted, binding);
            onChange();
            RefreshControls();
        }
        action.SelectedIndexChanged += (_, _) => Change();
        key.SelectedIndexChanged += (_, _) => Change();
        foreach (var check in checks)
            check.CheckedChanged += (_, _) => Change();
        return () =>
        {
            updating = true;
            editor.SuspendLayout();
            try
            {
                initial = Current.Button(selectedButton, shifted);
                heading.Text = $"G{selectedButton}";
                name.Text = Bindings.ButtonName(selectedButton);
                SelectChoice(action, initial.Action != 0 ? initial.Action : initial == default ? -1 : 0);
                SelectChoice(key, initial.Key);
                var keystroke = Selected(action) == 0;
                key.Enabled = keystroke;
                for (var bit = 0; bit < checks.Length; bit++)
                {
                    checks[bit].Enabled = keystroke;
                    checks[bit].Checked = (initial.Modifiers & (1 << bit)) != 0;
                }
            }
            finally
            {
                editor.ResumeLayout();
                updating = false;
            }
        };
    }

    private Action BuildDpiPage(FlowLayoutPanel pane)
    {
        var card = Card();
        var dpiOptions = new List<Choice> { new(0, "無効") };
        dpiOptions.AddRange(Enumerable.Range(4, 161).Select(step => new Choice(step * 50, $"{step * 50} DPI")));
        var header = LabelFor("", 10, Muted);
        card.Controls.Add(header);
        Space(card, 10);
        var row = Row();
        var slots = new ComboBox[4];
        for (var index = 0; index < 4; index++)
        {
            var slot = index;
            slots[index] = AddChoice(row, $"スロット {slot + 1}", dpiOptions, value => Current.SetDpi(slot, value), 112);
        }
        card.Controls.Add(row);
        Space(card, 12);
        var options = Row();
        var defaultSlot = AddChoice(options, "開始 DPI", Enumerable.Range(1, 4).Select(slot => new Choice(slot, $"スロット {slot}")),
            value => Current.DefaultSlot = (byte)value);
        var shift = AddChoice(options, "DPI シフト", dpiOptions, value => Current.ShiftDpi = value);
        var rate = AddChoice(options, "USB レポートレート", Enumerable.Range(0, 8).Select(rate => new Choice(rate, $"{1000 / (rate + 1)} Hz")),
            value => Current.Rate = (byte)value, 158);
        card.Controls.Add(options);
        pane.Controls.Add(card);

        var live = Card();
        live.Margin = new Padding(0, 14, 0, 0);
        live.Controls.Add(LabelFor("使用中の DPI を切り替え", 10, Ink, true));
        var actions = Row();
        var liveSlot = ChoiceFor("使用する保存済み DPI", 156, []);
        var apply = ButtonFor("この DPI を使用", 138);
        apply.Click += async (_, _) => await ActivateDpi(mode, Selected(liveSlot));
        actions.Controls.AddRange([liveSlot, apply]);
        live.Controls.Add(actions);
        pane.Controls.Add(live);
        return () =>
        {
            var stored = original[mode];
            header.Text = mode == activeMode && stored.Dpi(activeDpi) != 0
                ? $"使用中  •  {stored.Dpi(activeDpi)} DPI（スロット {activeDpi + 1}）"
                : "モードごとに最大 4 段階を保存できます。";
            for (var index = 0; index < slots.Length; index++)
                SelectChoice(slots[index], Current.Dpi(index));
            SelectChoice(defaultSlot, Current.DefaultSlot);
            SelectChoice(shift, Current.ShiftDpi);
            SelectChoice(rate, Current.Rate);

            var available = Enumerable.Range(0, 4).Where(slot => stored.Dpi(slot) != 0)
                .Select(slot => new Choice(slot, $"{stored.Dpi(slot)} DPI  /  {slot + 1}")).ToArray();
            live.Visible = mode == activeMode && available.Length > 1;
            if (mode != activeMode || available.Length <= 1)
                return;
            if (!liveSlot.Items.Cast<Choice>().SequenceEqual(available))
            {
                liveSlot.Items.Clear();
                liveSlot.Items.AddRange(available);
            }
            SelectChoice(liveSlot, available.Any(choice => choice.Value == activeDpi) ? activeDpi : available[0].Value);
        };
    }

    private Action BuildLightingPage(FlowLayoutPanel pane)
    {
        var card = Card();
        var colors = Row();
        var normal = AddColor(colors, "通常時の色", false);
        var shiftedColor = AddColor(colors, "G シフト中の色", true);
        card.Controls.Add(colors);
        Space(card, 12);
        var options = Row();
        var effect = AddChoice(options, "発光モード", [new(0, "固定色"), new(1, "明滅"), new(2, "色を巡回")],
            value => Current.Effect = (byte)value);
        var period = AddChoice(options, "発光周期", Enumerable.Range(0, 16).Select(seconds => new Choice(seconds, $"{seconds} 秒")),
            value => Current.Period = (byte)value);
        effect.SelectedIndexChanged += (_, _) => period.Enabled = Current.Effect is 1 or 2;
        card.Controls.Add(options);
        Space(card, 10);
        card.Controls.Add(LabelFor("消灯するには「固定色」を選び、色を #000000 に設定します。", 9, Muted));
        pane.Controls.Add(card);
        return () =>
        {
            normal();
            shiftedColor();
            SelectChoice(effect, Current.Effect);
            SelectChoice(period, Current.Period);
            period.Enabled = Current.Effect is 1 or 2;
        };
    }

    private Action AddColor(FlowLayoutPanel parent, string label, bool shift)
    {
        var column = Stack();
        column.Margin = new Padding(0, 0, 14, 0);
        column.Controls.Add(LabelFor(label, 10, Muted, true));
        var button = ButtonFor("", 140);
        void Display()
        {
            var color = Current.Led(shift);
            button.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            button.BackColor = color;
            button.ForeColor = color.GetBrightness() > 0.52 ? Color.Black : Color.White;
        }
        button.Click += (_, _) =>
        {
            using var picker = new ColorDialog { Color = Current.Led(shift), FullOpen = true };
            if (picker.ShowDialog(this) != DialogResult.OK)
                return;
            Current.SetLed(shift, picker.Color);
            Display();
            RefreshControls();
        };
        column.Controls.Add(button);
        parent.Controls.Add(column);
        return Display;
    }

    private ComboBox AddChoice(FlowLayoutPanel parent, string label, IEnumerable<Choice> options, Action<int> change, int width = 135)
    {
        var column = Stack();
        column.Margin = new Padding(0, 0, 13, 0);
        column.Controls.Add(LabelFor(label, 9, Muted, true));
        var field = ChoiceFor(label, width, options);
        field.SelectedIndexChanged += (_, _) =>
        {
            if (refreshingPage || field.SelectedItem is not Choice choice)
                return;
            change(choice.Value);
            RefreshControls();
        };
        column.Controls.Add(field);
        parent.Controls.Add(column);
        return field;
    }

    private sealed record Choice(int Value, string Caption, bool ExistingOnly = false)
    {
        public override string ToString() => Caption;
    }

    private static int Selected(ComboBox box) => ((Choice)box.SelectedItem!).Value;

    private static void SelectChoice(ComboBox box, int value)
    {
        if (box.SelectedItem is Choice selected && selected.Value == value)
            return;

        var last = box.Items.Count - 1;
        if (last >= 0 && box.Items[last] is Choice { ExistingOnly: true })
            box.Items.RemoveAt(last);
        for (var index = 0; index < box.Items.Count; index++)
        {
            if (box.Items[index] is Choice choice && choice.Value == value)
            {
                box.SelectedIndex = index;
                return;
            }
        }
        box.SelectedIndex = box.Items.Add(new Choice(value, $"既存値 ({value})", ExistingOnly: true));
    }

    private static ComboBox ChoiceFor(string label, int width, IEnumerable<Choice> items)
    {
        var box = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, BackColor = ControlColor, ForeColor = Ink,
            DrawMode = DrawMode.OwnerDrawFixed, FlatStyle = FlatStyle.Flat, Width = width,
            AccessibleName = label, MaxDropDownItems = 14,
            Margin = new Padding(0, 0, 0, 6)
        };
        box.DrawItem += (_, item) =>
        {
            var selected = (item.State & DrawItemState.Selected) != 0;
            using var fill = new SolidBrush(selected ? AccentBackground : ControlColor);
            item.Graphics.FillRectangle(fill, item.Bounds);
            if (item.Index >= 0)
            {
                var bounds = new Rectangle(item.Bounds.X + 5, item.Bounds.Y, item.Bounds.Width - 8, item.Bounds.Height);
                TextRenderer.DrawText(item.Graphics, box.GetItemText(box.Items[item.Index]), box.Font, bounds,
                    !box.Enabled ? Muted : selected ? Accent : Ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
            item.DrawFocusRectangle();
        };
        box.Items.AddRange(items.ToArray());
        return box;
    }

    private static FlowLayoutPanel Card() => OpaqueSurface(Stack(new Padding(16)), Surface);

    private static T OpaqueSurface<T>(T panel, Color color) where T : Panel
    {
        panel.BackColor = color;
        panel.HandleCreated += (_, _) => panel.BeginInvoke(() =>
        {
            // Let the panel create its child controls before redirecting their paint output.
            if (panel.IsHandleCreated)
                WindowBackdrop.MakeOpaque(panel.Handle);
        });
        return panel;
    }

    private static FlowLayoutPanel Stack(Padding padding = default) => new()
    {
        FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = padding
    };

    private static FlowLayoutPanel Row() => new()
    {
        FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty
    };

    private static void Space(FlowLayoutPanel panel, int height) =>
        panel.Controls.Add(new Panel { Width = 1, Height = height, Margin = Padding.Empty, TabStop = false });

    private static Label LabelFor(string text, float size, Color color, bool bold = false) => new()
    {
        Text = text, ForeColor = color, AutoSize = true, Margin = new Padding(0, 0, 0, 5),
        Font = new Font("Yu Gothic UI", size, bold ? FontStyle.Bold : FontStyle.Regular)
    };

    private static void Highlight(Button button, bool selected)
    {
        var background = selected ? AccentBackground : ControlColor;
        var border = selected ? Accent : ControlColor;
        if (button.BackColor != background)
            button.BackColor = background;
        if (button.ForeColor != (selected ? Accent : Ink))
            button.ForeColor = selected ? Accent : Ink;
        if (button.FlatAppearance.BorderColor != border)
            button.FlatAppearance.BorderColor = border;
    }

    private static Button ButtonFor(string text, int width, bool primary = false)
    {
        var button = new Button
        {
            Text = text, Size = new Size(width, 35), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = primary ? Accent : ControlColor, ForeColor = primary ? Canvas : Ink,
            Margin = new Padding(0, 0, 7, 6), UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? Accent : ControlColor;
        return button;
    }
}
