﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace YEJI_AW_Client
{
    /// <summary>
    /// 브라우저 URL 모니터링 클래스
    /// Chrome, Edge, Firefox 등의 브라우저에서 현재 활성화된 탭의 URL을 추출합니다.
    /// </summary>
    public class BrowserUrlMonitor
    {
        // UI Automation 호출 타임아웃 (밀리초) - Chrome 구버전 호환성 문제 방지
        private const int UiAutomationTimeoutMs = 1000;
        private const int EmailExtractionTimeoutMs = 2000;

        // SetForegroundWindow 후 브라우저가 포커스를 받을 때까지 대기하는 시간 (밀리초)
        private const int TabOperationForegroundDelayMs = 80;
        private const int TabOperationFocusRetryCount = 5;
        private const int TabOperationSendRetryCount = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("oleacc.dll")]
        private static extern IntPtr AccessibleObjectFromWindow(
            IntPtr hwnd,
            uint dwObjectID,
            byte[] riid,
            ref IntPtr ptr);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

        private const uint OBJID_WINDOW = 0x00000000;
        private const uint WM_CLOSE = 0x0010;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const uint WM_SYSKEYDOWN = 0x0104;
        private const uint WM_SYSKEYUP = 0x0105;
        private const int VK_MENU = 0x12;
        private const int VK_CONTROL = 0x11;        
        private const int VK_LEFT = 0x25;       
        private const int VK_W = 0x57;
        private const int VK_F4 = 0x73;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private static readonly HashSet<string> SupportedBrowsers = new()
        {
             "chrome", "msedge", "firefox", "opera", "brave", "iexplore", "whale"
        };

        // Chrome 버전 캐시 (프로세스당 한 번만 체크)
        private static readonly Dictionary<string, string?> browserVersionCache = new();
        private static readonly object versionCacheLock = new();

        // UI Automation 타임아웃/실패 추적 (예방 조치)
        private static int consecutiveTimeouts = 0;
        private static DateTime? suspendedUntil = null;
        private static readonly object timeoutTrackingLock = new();
        private const int MaxConsecutiveTimeouts = 5;  // 연속 5회 타임아웃 시 일시 중단
        private const int SuspensionMinutes = 5;        // 5분간 중단
        private static readonly bool EmailDebugLoggingEnabled =
            string.Equals(Environment.GetEnvironmentVariable("YEJI_EMAIL_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);
        private static readonly Regex EmailRegex = new Regex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 현재 활성화된 브라우저 창에서 URL을 추출합니다.
        /// </summary>
        /// <returns>URL 문자열, 추출 실패 시 null</returns>
        public static string? GetCurrentBrowserUrl()
        {
            try
            {
                // Circuit Breaker 패턴: 연속 타임아웃 발생 시 일시 중단
                lock (timeoutTrackingLock)
                {
                    if (suspendedUntil.HasValue)
                    {
                        if (DateTime.Now < suspendedUntil.Value)
                        {
                            // 아직 중단 기간 중
                            return null;
                        }
                        else
                        {
                            // 중단 기간 종료 - 재시도 허용
                            suspendedUntil = null;
                            consecutiveTimeouts = 0;
                            ClientLogger.LogAgent("Browser monitoring resumed after suspension period.", "INF");
                        }
                    }
                }

                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return null;

                GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == 0)
                    return null;

                using var process = Process.GetProcessById((int)processId);
                string processName = process.ProcessName.ToLowerInvariant();

                // 지원되는 브라우저인지 확인
                if (!IsSupportedBrowser(processName))
                    return null;

                // Chrome인 경우 버전 체크 (최초 1회만 로그 기록)
                if (processName.Equals("chrome", StringComparison.OrdinalIgnoreCase))
                {
                    GetBrowserVersion(processName); // 버전 정보 캐싱 및 로그 기록
                }             

                string? automationUrl = TryGetUrlFromUiAutomationWithTimeout(hwnd, processName);
                if (!string.IsNullOrWhiteSpace(automationUrl))
                {
                    // 성공 시 타임아웃 카운터 리셋
                    lock (timeoutTrackingLock)
                    {
                        consecutiveTimeouts = 0;
                    }
                    return automationUrl;
                }

                // 창 제목에서 URL 추출 시도
                string? url = ExtractUrlFromWindowTitle(hwnd, processName);

                return url;
            }
            catch
            {
                return null;
            }
        }

        public static IntPtr GetForegroundBrowserWindowHandle()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return IntPtr.Zero;

                GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == 0)
                    return IntPtr.Zero;

                using var process = Process.GetProcessById((int)processId);
                string processName = process.ProcessName.ToLowerInvariant();

                if (!IsSupportedBrowser(processName))
                    return IntPtr.Zero;

                return hwnd;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public static bool TryCloseBrowserWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            return PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public static bool TryCloseCurrentBrowserTab(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            // AllowSetForegroundWindow(ASFW_ANY)가 이미 UI 스레드에서 호출되었으므로
            // 백그라운드 스레드에서도 SetForegroundWindow가 정상 동작한다.
            // 브라우저가 실제 포그라운드를 되찾을 때까지 여러 번 재시도해 안정성을 높인다.
            bool focused = false;
            for (int i = 0; i < TabOperationFocusRetryCount; i++)
            {
                if (TryFocusWindow(hwnd))
                {
                    focused = true;
                    break;
                }

                Thread.Sleep(TabOperationForegroundDelayMs);
            }

            if (!focused)
            {
                ClientLogger.LogAgent("Failed to focus browser window before Ctrl+W tab close.", "WRN");
            }

            // 영업금지 URL 팝업이 표시되는 동안 사용자가 탭을 전환할 수 없으므로
            // 팝업이 닫힌 시점에도 영업금지 URL 탭이 활성 탭이다.
            // Ctrl+W로 현재 활성 탭(영업금지 URL 탭)만 닫는다.
            for (int i = 0; i < TabOperationSendRetryCount; i++)
            {
                if (SendCtrlW() || SendCtrlF4())
                {
                    return true;
                }

                Thread.Sleep(TabOperationForegroundDelayMs);
            }

            // 일부 브라우저(예: whale)에서는 SendInput이 차단되거나 누락되는 경우가 있어
            // 대상 창으로 키 메시지를 직접 전송하는 폴백 경로를 추가한다.
            for (int i = 0; i < TabOperationSendRetryCount; i++)
            {
                if (SendCtrlWViaPostMessage(hwnd) || SendCtrlF4ViaPostMessage(hwnd))
                {
                    return true;
                }

                Thread.Sleep(TabOperationForegroundDelayMs);
            }

            return false;
        }

        private static bool TryFocusWindow(IntPtr hwnd)
        {
            IntPtr currentForeground = GetForegroundWindow();
            if (currentForeground == hwnd)
            {
                return true;
            }

            uint currentThreadId = GetCurrentThreadId();
            uint foregroundThreadId = currentForeground == IntPtr.Zero
                ? 0
                : GetWindowThreadProcessId(currentForeground, IntPtr.Zero);
            uint targetThreadId = GetWindowThreadProcessId(hwnd, IntPtr.Zero);

            bool attachedToForeground = false;
            bool attachedToTarget = false;

            try
            {
                if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
                {
                    attachedToForeground = AttachThreadInput(currentThreadId, foregroundThreadId, true);
                }

                if (targetThreadId != 0 && targetThreadId != currentThreadId)
                {
                    attachedToTarget = AttachThreadInput(currentThreadId, targetThreadId, true);
                }

                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attachedToTarget)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, false);
                }

                if (attachedToForeground)
                {
                    AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
            }

            Thread.Sleep(TabOperationForegroundDelayMs);
            return GetForegroundWindow() == hwnd;
        }

        private static bool SendCtrlW()
        {
            return SendCtrlModifiedKey(VK_W);
        }

        private static bool SendCtrlF4()
        {
            return SendCtrlModifiedKey(VK_F4);
        }

        private static bool SendCtrlModifiedKey(int key)
        {
            int inputSize = Marshal.SizeOf(typeof(INPUT));

            // 일부 환경에서 Ctrl Down과 타겟 키 Down이 너무 붙어 전송되면
            // Ctrl 인식이 누락되어 단일 문자 입력(예: 'w')으로 처리되는 경우가 있어
            // 입력을 단계별로 보내고 짧은 지연을 둬 조합키 인식 안정성을 높인다.
            var ctrlDown = new[]
            {
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)VK_CONTROL, dwFlags = KEYEVENTF_EXTENDEDKEY } }
                }
            };

            var keyDown = new[]
            {
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)key } } }
            };

            var keyUp = new[]
            {
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)key, dwFlags = KEYEVENTF_KEYUP } } }
            };

            var ctrlUp = new[]
            {
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new InputUnion
                    {
                        ki = new KEYBDINPUT { wVk = (ushort)VK_CONTROL, dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP }
                    }
                }
            };

            if (SendInput(1, ctrlDown, inputSize) != 1)
                return false;

            Thread.Sleep(20);

            if (SendInput(1, keyDown, inputSize) != 1)
                return false;

            if (SendInput(1, keyUp, inputSize) != 1)
                return false;

            Thread.Sleep(20);

            return SendInput(1, ctrlUp, inputSize) == 1;
        }

        private static bool SendCtrlWViaPostMessage(IntPtr hwnd)
        {
            return PostMessage(hwnd, WM_KEYDOWN, new IntPtr(VK_CONTROL), IntPtr.Zero)
                && PostMessage(hwnd, WM_KEYDOWN, new IntPtr(VK_W), IntPtr.Zero)
                && PostMessage(hwnd, WM_KEYUP, new IntPtr(VK_W), IntPtr.Zero)
                && PostMessage(hwnd, WM_KEYUP, new IntPtr(VK_CONTROL), IntPtr.Zero);
        }

        private static bool SendCtrlF4ViaPostMessage(IntPtr hwnd)
        {
            return PostMessage(hwnd, WM_KEYDOWN, new IntPtr(VK_CONTROL), IntPtr.Zero)
                && PostMessage(hwnd, WM_KEYDOWN, new IntPtr(VK_F4), IntPtr.Zero)
                && PostMessage(hwnd, WM_KEYUP, new IntPtr(VK_F4), IntPtr.Zero)
                && PostMessage(hwnd, WM_KEYUP, new IntPtr(VK_CONTROL), IntPtr.Zero);
        }

        public static bool TrySendBrowserBack(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            SetForegroundWindow(hwnd);
            PostMessage(hwnd, WM_SYSKEYDOWN, new IntPtr(VK_MENU), IntPtr.Zero);
            PostMessage(hwnd, WM_KEYDOWN, new IntPtr(VK_LEFT), IntPtr.Zero);
            PostMessage(hwnd, WM_KEYUP, new IntPtr(VK_LEFT), IntPtr.Zero);
            PostMessage(hwnd, WM_SYSKEYUP, new IntPtr(VK_MENU), IntPtr.Zero);
            return true;
        }

        /// <summary>
        /// UI Automation을 사용한 URL 추출을 타임아웃과 함께 실행합니다.
        /// Chrome 구버전에서 발생할 수 있는 멈춤 현상을 방지합니다.
        /// </summary>
        private static string? TryGetUrlFromUiAutomationWithTimeout(IntPtr hwnd, string browserName)
        {
            using var cts = new CancellationTokenSource();
            try
            {
                // Task를 사용하여 타임아웃 적용
                var task = Task.Run(() => TryGetUrlFromUiAutomation(hwnd, browserName), cts.Token);

                if (task.Wait(UiAutomationTimeoutMs))
                {
                    return task.Result;
                }
                else
                {
                    // 타임아웃 발생 - 작업 취소 시도
                    cts.Cancel();

                    // 연속 타임아웃 추적 (Circuit Breaker 패턴)
                    lock (timeoutTrackingLock)
                    {
                        consecutiveTimeouts++;

                        if (consecutiveTimeouts >= MaxConsecutiveTimeouts)
                        {
                            suspendedUntil = DateTime.Now.AddMinutes(SuspensionMinutes);
                            ClientLogger.LogAgent(
                                $"Browser monitoring suspended for {SuspensionMinutes} minutes due to {consecutiveTimeouts} consecutive timeouts. " +
                                $"This usually indicates browser compatibility issues. Consider updating {browserName} to the latest version.",
                                "WRN");
                        }
                        else
                        {
                            // 로그 기록 (Chrome 구버전 호환성 문제 가능성)
                            ClientLogger.LogAgent(
                                $"Browser URL extraction timed out for {browserName} (timeout #{consecutiveTimeouts}). " +
                                $"Consider updating browser to latest version.",
                                "WRN");
                        }
                    }

                    return null;
                }
            }
            catch (AggregateException ex)
            {
                // Task 내부 예외 처리
                ClientLogger.LogAgent($"Browser URL extraction failed for {browserName}: {ex.InnerException?.Message}", "WRN");
                return null;
            }
            catch (Exception ex)
            {
                ClientLogger.LogAgent($"Browser URL extraction error for {browserName}: {ex.Message}", "WRN");
                return null;
            }
        }

        /// <summary>
        /// 현재 활성화된 브라우저 화면의 UI Automation 텍스트에서 이메일 주소를 추출합니다.
        /// 메일 작성 화면의 받는사람/참조/숨은참조 검사용으로 사용합니다.
        /// </summary>
        public static List<string> GetCurrentBrowserEmails()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return new List<string>();

                GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == 0)
                    return new List<string>();

                using var process = Process.GetProcessById((int)processId);
                string processName = process.ProcessName.ToLowerInvariant();
                if (!IsSupportedBrowser(processName))
                    return new List<string>();

                using var cts = new CancellationTokenSource();
                var task = Task.Run(() => ExtractEmailsFromUiAutomation(hwnd), cts.Token);
                if (task.Wait(EmailExtractionTimeoutMs))
                {
                    if (EmailDebugLoggingEnabled)
                    {
                        ClientLogger.LogAgent($"[EMAIL][DEBUG] Browser email extraction completed: {task.Result.Count} recipients found.", "DBG");
                    }
                    return task.Result;
                }

                cts.Cancel();
                if (EmailDebugLoggingEnabled)
                {
                    ClientLogger.LogAgent("[EMAIL][DEBUG] Browser email extraction timed out.", "DBG");
                }
            }
            catch (Exception ex)
            {
                if (EmailDebugLoggingEnabled)
                {
                    ClientLogger.LogAgent($"[EMAIL][DEBUG] Browser email extraction failed: {ex.Message}", "DBG");
                }
            }

            return new List<string>();
        }

        private static List<string> ExtractEmailsFromUiAutomation(IntPtr hwnd)
        {
            var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                if (root == null)
                    return new List<string>();

                // 메일 수신자 토큰(UI chip)이 Text/Edit가 아닌 경우가 많아 전체 트리에서 Name/Value를 폭넓게 스캔
                var elements = root.FindAll(TreeScope.Subtree, Condition.TrueCondition);
                int maxScan = Math.Min(elements.Count, 1200);
                for (int i = 0; i < maxScan; i++)
                {
                    var element = elements[i];

                    TryAddEmailsFromText(element.Current.Name, extracted);
                    TryAddEmailsFromText(element.Current.HelpText, extracted);
                    TryAddEmailsFromText(element.Current.ItemStatus, extracted);

                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObj) && valuePatternObj is ValuePattern valuePattern)
                    {
                        TryAddEmailsFromText(valuePattern.Current.Value, extracted);
                    }

                    // .NET/Windows 버전에 따라 LegacyIAccessiblePattern 타입이 제공되지 않을 수 있어
                    // Name/HelpText/ItemStatus/ValuePattern 중심으로 안전하게 추출합니다.
                }
            }
            catch
            {
                // ignore
            }

            return extracted.ToList();
        }

        private static void TryAddEmailsFromText(string? text, HashSet<string> output)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            foreach (Match match in EmailRegex.Matches(text))
            {
                string email = match.Value.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(email))
                {
                    output.Add(email);
                }
            }
        }

        private static string? TryGetUrlFromUiAutomation(IntPtr hwnd, string browserName)
        {
            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                if (root == null)
                    return null;

                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);

                var edits = root.FindAll(TreeScope.Subtree, condition);
                foreach (AutomationElement edit in edits)
                {
                    if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
                    {
                        string value = valuePattern.Current.Value;
                        if (IsLikelyUrl(value))
                            return value.Trim();
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static bool IsLikelyUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return true;

            if (trimmed.Contains(' ') || trimmed.Contains('	'))
                return false;

            return trimmed.Contains('.') && trimmed.Length > 3;
        }
        /// <summary>
        /// 프로세스 이름이 지원되는 브라우저인지 확인합니다.
        /// </summary>
        private static bool IsSupportedBrowser(string processName)
        {
            return SupportedBrowsers.Contains(processName);
        }

        /// <summary>
        /// 창 제목에서 URL을 추출합니다.
        /// </summary>
        private static string? ExtractUrlFromWindowTitle(IntPtr hwnd, string browserName)
        {
            try
            {
                int length = GetWindowTextLength(hwnd);
                if (length == 0)
                    return null;

                var sb = new StringBuilder(length + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                string title = sb.ToString();

                if (string.IsNullOrWhiteSpace(title))
                    return null;

                // 브라우저별로 제목에서 URL 추출
                // 대부분의 브라우저는 "페이지 제목 - URL" 또는 "URL - 브라우저 이름" 형식 사용
                string url = ParseUrlFromTitle(title, browserName);

                return url;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 브라우저 창 제목에서 URL을 파싱합니다.
        /// </summary>
        private static string ParseUrlFromTitle(string title, string browserName)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            // URL 패턴 검색 (http:// 또는 https://)
            int httpIndex = title.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
            int httpsIndex = title.IndexOf("https://", StringComparison.OrdinalIgnoreCase);

            int urlStartIndex = -1;
            if (httpsIndex >= 0)
                urlStartIndex = httpsIndex;
            else if (httpIndex >= 0)
                urlStartIndex = httpIndex;

            if (urlStartIndex >= 0)
            {
                // URL 시작점을 찾았으면 끝까지 또는 공백/특수문자까지 추출
                int urlEndIndex = title.Length;
                for (int i = urlStartIndex; i < title.Length; i++)
                {
                    char c = title[i];
                    if (c == ' ' || c == '\t' || c == '|' || c == '-')
                    {
                        urlEndIndex = i;
                        break;
                    }
                }

                string extractedUrl = title.Substring(urlStartIndex, urlEndIndex - urlStartIndex).Trim();
                if (!string.IsNullOrWhiteSpace(extractedUrl))
                    return extractedUrl;
            }

            // URL이 제목에 명시적으로 없는 경우, 제목 자체를 반환
            // (일부 경우 도메인만 표시되기도 함)
            return title;
        }

        /// <summary>
        /// URL에서 도메인을 추출합니다.
        /// </summary>
        public static string? ExtractDomain(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                {
                    return uri.Host.ToLowerInvariant();
                }

                // URL이 http:// 없이 시작하는 경우 (예: www.example.com)
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                    if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                    {
                        return uri.Host.ToLowerInvariant();
                    }
                }
            }
            catch
            {
                // URI 파싱 실패
            }

            return null;
        }

        private static string NormalizeDomain(string? domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return string.Empty;

            string normalized = domain.Trim().ToLowerInvariant();
            if (normalized.StartsWith("www.", StringComparison.Ordinal))
                normalized = normalized.Substring(4);

            return normalized;
        }

        private static bool TryCreateUri(string url, out Uri? uri)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                return true;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.TryCreate("https://" + url, UriKind.Absolute, out uri);
            }

            return false;
        }

        private static string NormalizeUrlHost(string url)
        {
            if (!TryCreateUri(url, out Uri? uri) || uri == null)
                return url.ToLowerInvariant();

            var builder = new UriBuilder(uri)
            {
                Host = NormalizeDomain(uri.Host)
            };

            return builder.Uri.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// URL이 금지된 URL 목록에 포함되는지 확인합니다.
        /// 도메인뿐만 아니라 서브도메인 및 하부 경로까지 체크합니다.
        /// </summary>
        public static bool IsProhibitedUrl(string? url, List<string> prohibitedUrls)
        {
            if (string.IsNullOrWhiteSpace(url) || prohibitedUrls == null || prohibitedUrls.Count == 0)
                return false;

            string? domain = ExtractDomain(url);
            if (string.IsNullOrWhiteSpace(domain))
                return false;

            string normalizedDomain = NormalizeDomain(domain);

            // URL 전체를 소문자로 변환 (경로 포함)
            string urlLower = url.ToLowerInvariant();
            string normalizedUrlLower = NormalizeUrlHost(url);

            foreach (var prohibited in prohibitedUrls)
            {
                if (string.IsNullOrWhiteSpace(prohibited))
                    continue;

                string prohibitedLower = prohibited.ToLowerInvariant().Trim();
                string prohibitedNormalizedDomain = NormalizeDomain(ExtractDomain(prohibitedLower));
                bool prohibitedHasPath = false;
                string prohibitedNormalizedUrl = prohibitedLower;

                if (TryCreateUri(prohibitedLower, out Uri? prohibitedUri) && prohibitedUri != null)
                {
                    prohibitedNormalizedDomain = NormalizeDomain(prohibitedUri.Host);
                    prohibitedHasPath = !string.IsNullOrEmpty(prohibitedUri.AbsolutePath) &&
                        !prohibitedUri.AbsolutePath.Equals("/", StringComparison.Ordinal);
                    prohibitedNormalizedUrl = NormalizeUrlHost(prohibitedLower);
                }

                // 1. 전체 URL 패턴 매칭 (경로 포함)
                // 예: abc.abc.com/blog 형태의 금지 URL
                if (prohibitedHasPath)
                {
                    // 금지 URL이 경로를 포함하는 경우
                    if (urlLower.Contains(prohibitedLower) ||
                        normalizedUrlLower.Contains(prohibitedNormalizedUrl))
                        return true;

                    // 경로가 포함된 금지 URL은 도메인 단독 매칭으로 확장하지 않음
                    continue;
                }

                // 2. 도메인만 지정된 경우 (경로 무관하게 차단)
                // 정확한 도메인 일치 (domain은 이미 ToLowerInvariant 처리됨)
                if (normalizedDomain.Equals(prohibitedNormalizedDomain, StringComparison.Ordinal))
                    return true;

                // 하위 도메인 포함 (예: abc.abc.com을 abc.com으로 차단)
                if (!string.IsNullOrEmpty(prohibitedNormalizedDomain) &&
                    normalizedDomain.EndsWith("." + prohibitedNormalizedDomain, StringComparison.Ordinal))
                    return true;

                // 와일드카드 패턴 지원 (예: *.example.com 형식의 금지 URL)
                if (prohibitedLower.StartsWith("*."))
                {
                    string baseDomain = NormalizeDomain(prohibitedLower.Substring(2));
                    if (normalizedDomain.Equals(baseDomain, StringComparison.Ordinal) ||
                        normalizedDomain.EndsWith("." + baseDomain, StringComparison.Ordinal))
                        return true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 브라우저 버전을 가져옵니다. (Chrome 호환성 문제 진단용)
        /// </summary>
        private static string? GetBrowserVersion(string processName)
        {
            try
            {
                lock (versionCacheLock)
                {
                    // 캐시에 있으면 반환
                    if (browserVersionCache.TryGetValue(processName, out string? cachedVersion))
                    {
                        return cachedVersion;
                    }
                }

                // Chrome 실행 파일 경로 찾기
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                    return null;

                try
                {
                    // 첫 번째 프로세스에서 버전 정보 가져오기
                    string? filePath = processes[0].MainModule?.FileName;

                    if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                        return null;

                    var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
                    string version = versionInfo.FileVersion ?? versionInfo.ProductVersion ?? "Unknown";

                    // 캐시에 저장
                    lock (versionCacheLock)
                    {
                        browserVersionCache[processName] = version;
                    }

                    // Chrome인 경우 버전 로그 기록
                    if (processName.Equals("chrome", StringComparison.OrdinalIgnoreCase))
                    {
                        ClientLogger.LogAgent($"Chrome browser version detected: {version}", "INF");
                    }

                    return version;
                }
                finally
                {
                    // 모든 프로세스 핸들 정리
                    foreach (var proc in processes)
                    {
                        proc.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                ClientLogger.LogAgent($"Failed to get browser version for {processName}: {ex.Message}", "DBG");
                return null;
            }
        }

        /// <summary>
        /// Chrome 버전이 최소 요구 버전보다 낮은지 확인합니다.
        /// 구버전 Chrome에서 UI Automation 호환성 문제가 있을 수 있습니다.
        /// </summary>
        public static bool IsOutdatedChromeVersion(int minMajorVersion = 120)
        {
            try
            {
                string? version = GetBrowserVersion("chrome");
                if (string.IsNullOrEmpty(version))
                    return false;

                // 버전 문자열에서 메이저 버전 추출 (예: "120.0.6099.130" -> 120)
                string[] parts = version.Split('.');
                if (parts.Length > 0 && int.TryParse(parts[0], out int majorVersion))
                {
                    if (majorVersion < minMajorVersion)
                    {
                        ClientLogger.LogAgent($"Chrome version {version} is outdated (minimum recommended: {minMajorVersion}). Please update to latest version to avoid compatibility issues.", "WRN");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                ClientLogger.LogAgent($"Failed to check Chrome version: {ex.Message}", "DBG");
                return false;
            }
        }

        /// <summary>
        /// 브라우저 모니터링이 현재 중단 상태인지 확인합니다.
        /// Circuit Breaker 패턴에 의해 연속 타임아웃 발생 시 일시 중단됩니다.
        /// </summary>
        public static bool IsBrowserMonitoringSuspended()
        {
            lock (timeoutTrackingLock)
            {
                if (suspendedUntil.HasValue && DateTime.Now < suspendedUntil.Value)
                {
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 브라우저 모니터링 상태 정보를 반환합니다. (진단용)
        /// </summary>
        public static string GetMonitoringStatus()
        {
            lock (timeoutTrackingLock)
            {
                if (suspendedUntil.HasValue && DateTime.Now < suspendedUntil.Value)
                {
                    var remainingTime = suspendedUntil.Value - DateTime.Now;
                    return $"Suspended (resumes in {remainingTime.TotalMinutes:F1} minutes, {consecutiveTimeouts} consecutive timeouts)";
                }
                else if (consecutiveTimeouts > 0)
                {
                    return $"Active (warning: {consecutiveTimeouts} recent timeout(s))";
                }
                else
                {
                    return "Active (healthy)";
                }
            }
        }
    }
}