using System.Reflection;
using Microsoft.Win32;

namespace G600Studio;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "G600 Studio";
    private static string Executable => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "G600Studio", "Startup", "G600Studio.exe");

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetRegistered(bool register)
    {
        if (!register)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            existing?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var source = Environment.ProcessPath;
        if (source is null || !string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location))
            throw new IOException("配布版の G600Studio.exe からスタートアップに登録してください。");

        var target = Executable;
        var command = $"\"{target}\" --startup";
        if (command.Length > 260)
            throw new IOException("スタートアップ用のファイルパスが長すぎます。");
        StageExecutable(source, target);
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    internal static void StageExecutable(string source, string target)
    {
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var staged = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(source, staged);
            File.Move(staged, target, overwrite: true);
        }
        finally
        {
            File.Delete(staged);
        }
    }
}
