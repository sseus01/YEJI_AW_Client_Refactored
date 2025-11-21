using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace YEJI_AW_Client
{
    internal static class MemoryOptimizer
    {
        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        public static void TrimWorkingSet()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                EmptyWorkingSet(process.Handle);
            }
            catch
            {
                // 최적화 실패는 무시
            }
        }
    }
}
