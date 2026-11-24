namespace G600Studio;

internal sealed class StudioApplication : ApplicationContext
{
    private readonly NotifyIcon? tray;
    private readonly ContextMenuStrip? trayMenu;
    private readonly Control dispatcher = new();
    private readonly RegisteredWaitHandle openRequest;
    private StudioForm? window;
    private bool exiting;

    public StudioApplication(bool startup, EventWaitHandle showWindow)
    {
        _ = dispatcher.Handle;
        openRequest = ThreadPool.RegisterWaitForSingleObject(showWindow, (_, _) =>
        {
            try { dispatcher.BeginInvoke(OpenWindow); }
            catch (InvalidOperationException) { } // The application closed before the request was dispatched.
        }, null, Timeout.Infinite, executeOnlyOnce: false);

        if (startup)
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("G600 Studio を開く", null, (_, _) => OpenWindow());
            trayMenu.Items.Add("終了", null, (_, _) => Quit());
            tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "G600 Studio", ContextMenuStrip = trayMenu, Visible = true };
            tray.MouseDoubleClick += (_, _) => OpenWindow();
        }
        else
        {
            dispatcher.BeginInvoke(OpenWindow);
        }
    }

    private void OpenWindow()
    {
        if (window is null)
        {
            window = new StudioForm();
            window.FormClosed += (_, _) =>
            {
                window = null;
                if (tray is null || exiting)
                    ExitThread();
            };
            window.Show();
        }
        if (window.WindowState == FormWindowState.Minimized)
            window.WindowState = FormWindowState.Normal;
        window.Activate();
    }

    private void Quit()
    {
        if (window is null)
        {
            ExitThread();
            return;
        }
        exiting = true;
        window.Close();
        exiting = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            openRequest.Unregister(null);
            dispatcher.Dispose();
            if (tray is not null)
            {
                tray.Visible = false;
                tray.Dispose();
                trayMenu!.Dispose();
            }
            window?.Dispose();
        }
        base.Dispose(disposing);
    }
}
