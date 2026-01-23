using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace YEJI_AW_Client
{
    /// <summary>
    /// 브라우저 URL 모니터링 클래스
    /// Chrome, Edge, Firefox 등의 브라우저에서 현재 활성화된 탭의 URL을 추출합니다.
    /// </summary>
    public class BrowserUrlMonitor
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

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
        private const int VK_LEFT = 0x25;

        private static readonly HashSet<string> SupportedBrowsers = new()
        {
             "chrome", "msedge", "firefox", "opera", "brave", "iexplore", "whale"
        };

        /// <summary>
        /// 현재 활성화된 브라우저 창에서 URL을 추출합니다.
        /// </summary>
        /// <returns>URL 문자열, 추출 실패 시 null</returns>
        public static string? GetCurrentBrowserUrl()
        {
            try
            {
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

                string? automationUrl = TryGetUrlFromUiAutomation(hwnd, processName);
                if (!string.IsNullOrWhiteSpace(automationUrl))
                {
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
    }
}