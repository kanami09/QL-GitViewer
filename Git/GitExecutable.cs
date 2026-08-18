using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;

namespace QuickLook.Plugin.GitViewer.Git
{
    /// <summary>
    ///     每个进程只定位一次 git.exe。查找顺序：PATH、Git for Windows 注册表项、常见安装路径。
    /// </summary>
    public static class GitExecutable
    {
        private static readonly object Lock = new object();
        private static bool _resolved;
        private static string _path;

        /// <summary>git.exe 的完整路径；未安装 git 时为 null。</summary>
        public static string Path
        {
            get
            {
                lock (Lock)
                {
                    if (!_resolved)
                    {
                        _path = Resolve();
                        _resolved = true;
                    }

                    return _path;
                }
            }
        }

        public static bool IsAvailable => Path != null;

        private static string Resolve()
        {
            try
            {
                foreach (var candidate in Candidates())
                {
                    if (string.IsNullOrEmpty(candidate))
                        continue;

                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            catch (Exception)
            {
                // 直接往下走：找不到 git 是一种受支持的状态，但在这里抛异常不是。
            }

            return null;
        }

        private static IEnumerable<string> Candidates()
        {
            // 1. PATH。直接枚举而不是调 where.exe，避免为了找 git 先启动一个进程。
            var pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathVariable))
                foreach (var dir in pathVariable.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir))
                        continue;

                    string combined = null;
                    try
                    {
                        combined = System.IO.Path.Combine(dir.Trim('"'), "git.exe");
                    }
                    catch (ArgumentException)
                    {
                        // PATH 里混进非法字符的条目很常见，跳过即可。
                    }

                    if (combined != null)
                        yield return combined;
                }

            // 2. Git for Windows 注册表项。"cmd\git.exe" 才是给外部调用者用的包装，
            //    "bin\git.exe" 和 "mingw64\bin\git.exe" 会把 MSYS 那一层也拖进来。
            foreach (var root in new[]
                     {
                         @"HKEY_LOCAL_MACHINE\SOFTWARE\GitForWindows",
                         @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GitForWindows",
                         @"HKEY_CURRENT_USER\SOFTWARE\GitForWindows"
                     })
            {
                string installPath = null;
                try
                {
                    installPath = Registry.GetValue(root, "InstallPath", null) as string;
                }
                catch (Exception)
                {
                    // 这个键读不了就试下一个。
                }

                if (!string.IsNullOrEmpty(installPath))
                    yield return System.IO.Path.Combine(installPath, @"cmd\git.exe");
            }

            // 3. 常见安装路径。
            foreach (var variable in new[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432" })
            {
                var programFiles = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(programFiles))
                    yield return System.IO.Path.Combine(programFiles, @"Git\cmd\git.exe");
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                yield return System.IO.Path.Combine(localAppData, @"Programs\Git\cmd\git.exe");
        }
    }
}
