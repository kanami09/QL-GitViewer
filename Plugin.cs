using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;
using QuickLook.Plugin.GitViewer.Git;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace QuickLook.Plugin.GitViewer
{
    /// <summary>
    ///     在 git 仓库的 ".git" 文件夹或工作区根目录上按空格时，预览这个仓库。
    /// </summary>
    public class Plugin : IViewer
    {
        private CancellationTokenSource _cts;
        private GitPanel _panel;
        private GitCommandRunner _runner;

        public int Priority => 0;

        public void Init()
        {
            // git.exe 是首次使用时才惰性解析的，启动阶段没有任何事要做。
        }

        public bool CanHandle(string path)
        {
            return GitRepositoryLocator.Locate(path) != null;
        }

        public void Prepare(string path, ContextObject context)
        {
            // PreferredSize 没有变更通知，而且在窗口显示前只被读取一次，
            // 所以只能在这里设置，写在 View 里是无效的。
            context.PreferredSize = new Size { Width = 900, Height = 620 };
            context.Theme = OSThemeHelper.AppsUseDarkTheme() ? Themes.Dark : Themes.Light;
            context.CanResize = true;
        }

        public void View(string path, ContextObject context)
        {
            // 本方法是通过 Dispatcher.BeginInvoke 在 UI 线程上调用的，必须立刻返回，
            // 把 git 相关的活儿全部丢给后台任务。
            var panel = new GitPanel();
            _panel = panel;
            context.ViewerContent = panel;

            // FindMatch 交给 Prepare/View 的是一个全新实例，CanHandle 阶段算出来的东西
            // 在这里一概拿不到，所以必须重新解析一次路径。
            var location = GitRepositoryLocator.Locate(path);
            if (location == null)
            {
                context.Title = path;
                panel.ShowError(Translate.Get("ErrorNotARepository", "This folder is not a git repository."));
                context.IsBusy = false;
                return;
            }

            context.Title = location.DisplayName;

            if (!GitExecutable.IsAvailable)
            {
                panel.ShowFallbackHeader(location);
                panel.ShowError(Translate.Get("ErrorNoGit",
                    "git.exe was not found. Install Git for Windows to preview repositories."));
                context.IsBusy = false;
                return;
            }

            _cts = new CancellationTokenSource();
            _runner = new GitCommandRunner(location.StartDirectory);

            var runner = _runner;
            var token = _cts.Token;

            Task.Run(() => Load(location, context, panel, runner, token));
        }

        public void Cleanup()
        {
            // 取消会杀掉还在跑的 git 进程，同时阻止加载任务去碰一个
            // QuickLook 已经为下一次预览重置过的 ContextObject。
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            if (_runner != null)
            {
                _runner.Dispose();
                _runner = null;
            }

            _panel = null;
        }

        /// <summary>
        ///     在后台线程上分两阶段读取仓库，让头部概览先于较慢的明细查询画出来。
        ///     写成静态方法，就不会和 Cleanup 清空实例字段的动作发生竞争。
        /// </summary>
        private static void Load(RepositoryLocation location, ContextObject context, GitPanel panel,
            GitCommandRunner runner, CancellationToken ct)
        {
            // 任何异常都不许逃出这个方法：QuickLook 只保护了 View 的同步部分，
            // 后台任务里未被观察的异常会把整个进程带崩。
            try
            {
                var reader = new GitRepositoryReader(runner);

                var overview = reader.ReadOverview(ct);
                if (ct.IsCancellationRequested)
                    return;

                Marshal(panel, ct, () =>
                {
                    panel.ApplyOverview(location, overview);
                    context.IsBusy = false;
                });

                var details = reader.ReadDetails(ct);
                if (ct.IsCancellationRequested)
                    return;

                Marshal(panel, ct, () => panel.ApplyDetails(details));
            }
            catch (Exception e)
            {
                ProcessHelper.WriteLog(string.Format(CultureInfo.InvariantCulture,
                    "GitViewer: failed to read {0}: {1}", location.StartDirectory, e));

                Marshal(panel, ct, () =>
                {
                    panel.ShowError(Translate.Get("ErrorReadFailed", "Could not read this repository.")
                                    + Environment.NewLine + e.Message);
                    context.IsBusy = false;
                });
            }
        }

        /// <summary>
        ///     把 <paramref name="action" /> 放到 UI 线程上执行，除非预览已经被换掉了。
        ///     在 dispatcher 回调内部还要再查一次取消标记：Cleanup 可能正好发生在
        ///     排队和执行之间，那时 ContextObject 已经属于下一次预览了。
        /// </summary>
        private static void Marshal(GitPanel panel, CancellationToken ct, Action action)
        {
            try
            {
                panel.Dispatcher.Invoke(() =>
                {
                    if (!ct.IsCancellationRequested)
                        action();
                });
            }
            catch (Exception)
            {
                // dispatcher 正在关闭，或者预览窗口已经没了。
            }
        }
    }
}
