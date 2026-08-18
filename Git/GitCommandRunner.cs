using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace QuickLook.Plugin.GitViewer.Git
{
    /// <summary>
    ///     在指定目录里执行只读的 git 命令并收集输出。
    ///     每次调用都是同步的；调用方本来就应该在后台线程上，需要并发时自行用 Task.Run 铺开。
    /// </summary>
    public sealed class GitCommandRunner : IDisposable
    {
        private const int DefaultTimeoutMs = 10000;

        /// <summary>反斜杠。写成字符码是为了让源码里不出现需要转义的字面量。</summary>
        private const char Backslash = (char)92;

        /// <summary>
        ///     每条命令都会带上的选项。
        ///     --no-optional-locks 保证我们不去写 index.lock，预览仓库时不会和用户开着的
        ///     编辑器或终端抢锁。
        ///     core.quotepath=false 让 git 不要把非 ASCII 路径转义成 \3xx 八进制。
        ///     log.showSignature=false 避免 gpg 的输出混进机器可读的日志里。
        /// </summary>
        private static readonly string[] GlobalArgs =
        {
            "--no-optional-locks",
            "-c", "core.quotepath=false",
            "-c", "color.ui=false",
            "-c", "log.showSignature=false"
        };

        private readonly object _lock = new object();
        private readonly List<Process> _running = new List<Process>();
        private readonly string _startDirectory;
        private bool _disposed;

        public GitCommandRunner(string startDirectory)
        {
            _startDirectory = startDirectory;
        }

        /// <summary>杀掉所有还没结束的命令。由 IViewer.Cleanup 调用。</summary>
        public void Dispose()
        {
            List<Process> snapshot;
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                snapshot = new List<Process>(_running);
                _running.Clear();
            }

            foreach (var process in snapshot)
                TryKill(process);
        }

        /// <summary>
        ///     用 <paramref name="args" /> 执行 git。退出码非 0 是返回而不是抛出的：
        ///     很多正常状态（没有上游、没有 stash、HEAD 未诞生）本来就以非 0 退出。
        /// </summary>
        public GitResult Run(CancellationToken ct, params string[] args)
        {
            if (_disposed || ct.IsCancellationRequested)
                return new GitResult { ExitCode = -1 };

            var exe = GitExecutable.Path;
            if (exe == null)
                return new GitResult { ExitCode = -1, StdErr = "git.exe not found" };

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = BuildArguments(args),
                WorkingDirectory = _startDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GIT_PAGER"] = "cat";
            startInfo.Environment["GCM_INTERACTIVE"] = "never";

            Process process = null;
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);

            try
            {
                process = new Process { StartInfo = startInfo };
                if (!process.Start())
                    return new GitResult { ExitCode = -1, StdErr = "failed to start git" };

                lock (_lock)
                {
                    if (_disposed)
                    {
                        TryKill(process);
                        return new GitResult { ExitCode = -1 };
                    }

                    _running.Add(process);
                }

                var target = process;
                registration = ct.Register(() => TryKill(target));

                // 这里的 git 从不读 stdin；关掉它，万一 git 真去读也不会挂住。
                process.StandardInput.Close();

                // 两个管道必须同时抽取，否则某条命令写满其中一个缓冲区后就会永远阻塞，
                // 而我们还在等另一个。
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(DefaultTimeoutMs))
                {
                    TryKill(process);
                    return new GitResult { ExitCode = -1, StdErr = "git timed out" };
                }

                // WaitForExit(int) 可能在异步读取器把数据刷完之前就返回；
                // 无参重载会连这部分一起等。
                process.WaitForExit();

                return new GitResult
                {
                    ExitCode = process.ExitCode,
                    StdOut = stdout.Result ?? string.Empty,
                    StdErr = stderr.Result ?? string.Empty
                };
            }
            catch (Exception e)
            {
                return new GitResult { ExitCode = -1, StdErr = e.Message };
            }
            finally
            {
                registration.Dispose();

                if (process != null)
                {
                    lock (_lock)
                    {
                        _running.Remove(process);
                    }

                    process.Dispose();
                }
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception)
            {
                // 进程已经没了，或者我们和它的退出抢跑输了。无论哪种都无事可做。
            }
        }

        private static string BuildArguments(string[] args)
        {
            var builder = new StringBuilder();

            foreach (var arg in GlobalArgs)
                AppendArgument(builder, arg);

            foreach (var arg in args)
                AppendArgument(builder, arg);

            return builder.ToString();
        }

        private static void AppendArgument(StringBuilder builder, string arg)
        {
            if (builder.Length > 0)
                builder.Append(' ');

            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                builder.Append(arg);
                return;
            }

            // Windows 命令行引用规则：反斜杠只有紧挨在引号前面时才有特殊含义，
            // 那种位置上必须成对加倍。
            builder.Append('"');
            for (var i = 0; i < arg.Length; i++)
            {
                var backslashes = 0;
                while (i < arg.Length && arg[i] == Backslash)
                {
                    backslashes++;
                    i++;
                }

                if (i == arg.Length)
                {
                    builder.Append(Backslash, backslashes * 2);
                    break;
                }

                if (arg[i] == '"')
                    builder.Append(Backslash, backslashes * 2 + 1);
                else
                    builder.Append(Backslash, backslashes);

                builder.Append(arg[i]);
            }

            builder.Append('"');
        }
    }
}
