namespace G600Studio;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var startup = args is ["--startup"];
        using var showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\G600Studio.ShowWindow", out var newEvent);
        using var mutex = new Mutex(true, @"Local\G600Studio", out var firstInstance);
        if (!firstInstance)
        {
            if (args is ["--probe", var busyLog])
            {
                File.WriteAllText(busyLog, "G600 Studio is already open.");
                Environment.ExitCode = 1;
            }
            else if (!startup)
            {
                if (newEvent)
                    MessageBox.Show("旧版の G600 Studio を終了してから開き直してください。", "G600 Studio");
                else
                    showWindow.Set();
            }
            return;
        }

        if (args is ["--probe", var file])
        {
            try
            {
                using var device = G600Device.Open();
                var result = new List<string> { "State " + Convert.ToHexString(device.ReadActive().Raw) };
                for (var index = 0; index < G600Device.ProfileCount; index++)
                    result.Add(Convert.ToHexString(device.ReadProfile(index)));
                File.WriteAllLines(file, result);
            }
            catch (Exception error)
            {
                File.WriteAllText(file, error.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }

        ApplicationConfiguration.Initialize();
        using var application = new StudioApplication(startup, showWindow);
        Application.Run(application);
    }
}
