using System.Windows.Forms;

namespace YEJI_AW_Client
{
    internal static class TrayIconCleanup
    {
        private static readonly object SyncRoot = new();
        private static NotifyIcon? notifyIcon;

        internal static void Register(NotifyIcon icon)
        {
            if (icon == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                notifyIcon = icon;
            }
        }

        internal static void Dispose()
        {
            lock (SyncRoot)
            {
                if (notifyIcon == null)
                {
                    return;
                }

                try
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }
                catch
                {
                    // 트레이 아이콘 정리 실패는 무시
                }
                finally
                {
                    notifyIcon = null;
                }
            }
        }
    }
}