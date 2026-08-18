using QuickLook.Common.Helpers;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuickLook.Plugin.GitViewer
{
    /// <summary>
    ///     直接调用 Win32 剪贴板 API 写入文本。
    ///     <para>
    ///     刻意不走 WPF 的 <c>System.Windows.Clipboard</c>：它内部对每一次调用都做
    ///     10 次、每次间隔 100ms 的重试（私有常量 OleRetryCount / OleRetryDelay），
    ///     所以一次失败就要阻塞约 1 秒。这些调用发生在 UI 线程上，界面会直接假死，
    ///     在外面再套一层重试更是把这个时间成倍放大。
    ///     </para>
    ///     <para>
    ///     Win32 路径失败时立即返回，重试的节奏完全由我们自己掌握，总上限约 100ms。
    ///     实测在 OLE 剪贴板整体不可用（WPF 与 WinForms 的读写全部失败）时，
    ///     这条路径依然能写入成功。
    ///     </para>
    /// </summary>
    internal static class ClipboardHelper
    {
        private const uint CfUnicodeText = 13;
        private const uint GmemMoveable = 0x0002;

        /// <summary>重试次数与间隔，总上限约 100ms —— 短到用户察觉不到。</summary>
        private const int Attempts = 5;
        private const int DelayMs = 20;

        /// <summary>
        ///     把 <paramref name="text" /> 写入剪贴板，成功返回 true。
        /// </summary>
        /// <param name="ownerWindow">
        ///     剪贴板属主窗口句柄。MSDN 指出传 IntPtr.Zero 时 EmptyClipboard 会把属主置空，
        ///     进而可能导致 SetClipboardData 失败，所以应尽量传入真实句柄；
        ///     拿不到时退回 IntPtr.Zero 仍然值得一试。
        /// </param>
        public static bool SetText(string text, IntPtr ownerWindow)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            var lastError = 0;

            for (var i = 0; i < Attempts; i++)
            {
                if (TrySetText(text, ownerWindow, out lastError))
                    return true;

                if (i < Attempts - 1)
                    Thread.Sleep(DelayMs);
            }

            ProcessHelper.WriteLog(string.Format(CultureInfo.InvariantCulture,
                "GitViewer: could not write to clipboard after {0} attempts, last Win32 error = {1}",
                Attempts, lastError));

            return false;
        }

        private static bool TrySetText(string text, IntPtr ownerWindow, out int lastError)
        {
            lastError = 0;

            if (!OpenClipboard(ownerWindow))
            {
                lastError = Marshal.GetLastWin32Error();
                return false;
            }

            var handle = IntPtr.Zero;
            try
            {
                if (!EmptyClipboard())
                {
                    lastError = Marshal.GetLastWin32Error();
                    return false;
                }

                // UTF-16 字符加上结尾的 null 终止符。
                var bytes = (text.Length + 1) * 2;

                handle = GlobalAlloc(GmemMoveable, new UIntPtr((uint)bytes));
                if (handle == IntPtr.Zero)
                {
                    lastError = Marshal.GetLastWin32Error();
                    return false;
                }

                var target = GlobalLock(handle);
                if (target == IntPtr.Zero)
                {
                    lastError = Marshal.GetLastWin32Error();
                    return false;
                }

                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    Marshal.WriteInt16(target, text.Length * 2, 0);
                }
                finally
                {
                    GlobalUnlock(handle);
                }

                if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
                {
                    lastError = Marshal.GetLastWin32Error();
                    return false;
                }

                // 到这里内存块的所有权已经转交给系统，绝不能再 GlobalFree，
                // 否则会释放掉剪贴板正在使用的内存。
                handle = IntPtr.Zero;
                return true;
            }
            finally
            {
                // 只有在所有权还没转交出去时才需要我们自己释放。
                if (handle != IntPtr.Zero)
                    GlobalFree(handle);

                CloseClipboard();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);
    }
}
