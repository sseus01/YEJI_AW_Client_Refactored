using System;
using System.Windows.Forms;
using System.IO;

public class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly HeartbeatWriter? _heartbeatWriter;

    public TrayAppContext(HeartbeatWriter? heartbeatWriter = null)
    {
        _heartbeatWriter = heartbeatWriter;

        // TODO: Replace Properties.Resources.AppIcon with actual 리소스 또는 Icon 파일 로드
        _trayIcon = new NotifyIcon()
        {
            Icon = Properties.Resources.AppIcon, // 대체: new Icon(Path.Combine(AppContext.BaseDirectory, "app.ico"))
            Text = "YEJI-ON",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        // 예외 처리 시 트레이 정리
        Application.ApplicationExit += OnApplicationExit;
        AppDomain.CurrentDomain.UnhandledException += (s, e) => OnApplicationExit(s, EventArgs.Empty);
        Application.ThreadException += (s, e) => OnApplicationExit(s, EventArgs.Empty);
        TaskScheduler.UnobservedTaskException += (s, e) => { OnApplicationExit(s, EventArgs.Empty); e.SetObserved(); };
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("열기", null, (s, e) => ShowMainWindow());
        menu.Items.Add("종료", null, (s, e) => ExitThread());
        return menu;
    }

    private void ShowMainWindow()
    {
        // 필요 시 메인 윈도우를 띄우는 로직 추가
    }

    protected override void ExitThreadCore()
    {
        OnApplicationExit(this, EventArgs.Empty);
        base.ExitThreadCore();
    }

    private void OnApplicationExit(object? sender, EventArgs e)
    {
        try
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
        }
        catch { }

        try
        {
            _heartbeatWriter?.Dispose();
        }
        catch { }
    }
}