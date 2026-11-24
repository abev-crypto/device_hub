using System.Text.Json;

namespace G600Studio;

internal sealed class Profile(byte[] bytes)
{
    public byte[] Bytes { get; } = bytes;
    public byte Effect { get => Bytes[4]; set => Bytes[4] = value; }
    public byte Period { get => Bytes[5]; set => Bytes[5] = value; }
    public byte Rate { get => Bytes[11]; set => Bytes[11] = value; }
    public int ShiftDpi { get => Bytes[12] * 50; set => Bytes[12] = EncodeDpi(value); }
    public byte DefaultSlot { get => Bytes[13]; set => Bytes[13] = value; }

    public int Dpi(int slot) => Bytes[14 + slot] * 50;
    public void SetDpi(int slot, int value) => Bytes[14 + slot] = EncodeDpi(value);
    public Profile Copy() => new((byte[])Bytes.Clone());

    public Color Led(bool shifted)
    {
        var offset = shifted ? 91 : 1;
        return Color.FromArgb(Bytes[offset], Bytes[offset + 1], Bytes[offset + 2]);
    }

    public void SetLed(bool shifted, Color color)
    {
        var offset = shifted ? 91 : 1;
        Bytes[offset] = color.R;
        Bytes[offset + 1] = color.G;
        Bytes[offset + 2] = color.B;
    }

    public Binding Button(int number, bool shifted)
    {
        var offset = ButtonOffset(number, shifted);
        return new(Bytes[offset], Bytes[offset + 1], Bytes[offset + 2]);
    }

    public void SetButton(int number, bool shifted, Binding value)
    {
        var offset = ButtonOffset(number, shifted);
        Bytes[offset] = value.Action;
        Bytes[offset + 1] = value.Modifiers;
        Bytes[offset + 2] = value.Key;
    }

    private static int ButtonOffset(int number, bool shifted) => (shifted ? 94 : 31) + (number - 1) * 3;

    private static byte EncodeDpi(int value)
    {
        if (value != 0 && (value < 200 || value > 8200 || value % 50 != 0))
            throw new InvalidDataException("DPI は 200〜8200 の範囲で 50 刻みです。0 は無効を示します。");
        return (byte)(value / 50);
    }
}

internal readonly record struct Binding(byte Action, byte Modifiers, byte Key)
{
    public string Caption
    {
        get
        {
            if (Action != 0)
                return Bindings.Actions.GetValueOrDefault(Action, $"機器設定 0x{Action:X2}");
            var parts = new List<string>();
            for (var bit = 0; bit < Bindings.ModifierNames.Length; bit++)
                if ((Modifiers & (1 << bit)) != 0)
                    parts.Add(Bindings.ModifierNames[bit]);
            if (Key != 0)
                parts.Add(Bindings.Keys.GetValueOrDefault(Key, $"HID 0x{Key:X2}"));
            return parts.Count == 0 ? "割り当てなし" : string.Join(" + ", parts);
        }
    }
}

internal static class Bindings
{
    public static readonly string[] ModifierNames = ["Ctrl", "Shift", "Alt", "Win", "右 Ctrl", "右 Shift", "右 Alt", "右 Win"];
    public static readonly Dictionary<byte, string> Actions = new()
    {
        [0x01] = "左クリック", [0x02] = "右クリック", [0x03] = "中クリック",
        [0x04] = "戻る", [0x05] = "進む",
        [0x11] = "DPI を上げる", [0x12] = "DPI を下げる", [0x13] = "DPI を巡回",
        [0x14] = "モードを巡回", [0x15] = "DPI シフト", [0x17] = "G シフト"
    };
    public static readonly Dictionary<byte, string> Keys = CreateKeys();

    public static string ButtonName(int number) => number switch
    {
        1 => "左クリック", 2 => "右クリック", 3 => "ホイール押し込み",
        4 => "ホイール左", 5 => "ホイール右", 6 => "右端ボタン",
        7 => "ホイール手前・上", 8 => "ホイール手前・下", _ => "サイドボタン"
    };

    private static Dictionary<byte, string> CreateKeys()
    {
        var keys = new Dictionary<byte, string> { [0] = "キーなし" };
        for (var i = 0; i < 26; i++)
            keys[(byte)(4 + i)] = ((char)('A' + i)).ToString();
        for (var i = 0; i < 10; i++)
            keys[(byte)(0x1e + i)] = ((i + 1) % 10).ToString();
        string[] common = ["Enter", "Esc", "Backspace", "Tab", "Space", "- / JIS ほ", "= / JIS へ", "[ / JIS @", "] / JIS [", "バックスラッシュ", "JIS ]", ";", "アポストロフィ", "半角/全角 (JIS)", ",", ".", "/", "Caps Lock"];
        for (var i = 0; i < common.Length; i++)
            keys[(byte)(0x28 + i)] = common[i];
        for (var i = 0; i < 12; i++)
            keys[(byte)(0x3a + i)] = $"F{i + 1}";
        string[] navigation = ["Print Screen", "Scroll Lock", "Pause", "Insert", "Home", "Page Up", "Delete", "End", "Page Down", "→", "←", "↓", "↑",
            "Num Lock", "Num /", "Num *", "Num -", "Num +", "Num Enter", "Num 1", "Num 2", "Num 3", "Num 4", "Num 5", "Num 6", "Num 7", "Num 8", "Num 9", "Num 0", "Num ."];
        for (var i = 0; i < navigation.Length; i++)
            keys[(byte)(0x46 + i)] = navigation[i];
        for (var i = 0; i < 12; i++)
            keys[(byte)(0x68 + i)] = $"F{i + 13}";
        keys[0x87] = "ろ (JIS)";
        keys[0x89] = "¥ (JIS)";
        return keys;
    }
}

internal sealed record BackupFile(string Format, int Mode, string Report)
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "G600Studio", "Backups");

    private const string ProfileFormat = "G600Studio-profile-1";

    public static string Save(int index, Profile profile)
    {
        Directory.CreateDirectory(DirectoryPath);
        var filename = Path.Combine(DirectoryPath, $"G600-mode-{index + 1}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
        var backup = new BackupFile(ProfileFormat, index + 1, Convert.ToHexString(profile.Bytes));
        using var stream = new FileStream(filename, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, backup, new JsonSerializerOptions { WriteIndented = true });
        stream.Flush(true);
        return filename;
    }

    public static Profile Read(string filename, int index)
    {
        using var stream = File.OpenRead(filename);
        if (stream.Length > 4096)
            throw new InvalidDataException("G600 Studio のバックアップファイルを選んでください。");
        var backup = JsonSerializer.Deserialize<BackupFile>(stream);
        if (backup is not { Format: ProfileFormat, Report.Length: G600Device.ProfileSize * 2 })
            throw new InvalidDataException("G600 Studio のバックアップ形式ではありません。");
        var report = Convert.FromHexString(backup.Report);
        if (backup.Mode != index + 1 || report[0] != G600Device.ProfileId(index))
            throw new InvalidDataException("バックアップと同じモードを選んでください。");
        return new Profile(report);
    }
}
