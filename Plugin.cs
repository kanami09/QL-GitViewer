using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;
using QuickLook.Plugin.GitViewer.Git;
using QuickLook.Plugin.GitViewer.Helpers;
using QuickLook.Plugin.GitViewer.ViewModels;
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
                panel.Model.ErrorMessage =
                    Translate.Get("ErrorNotARepository", "This folder is not a git repository.");
                context.IsBusy = false;
                return;
            }

            context.Title = location.DisplayName;

            if (!GitExecutable.IsAvailable)
            {
                panel.Model.ApplyFallbackHeader(location);
                panel.Model.ErrorMessage = Translate.Get("ErrorNoGit",
                    "git.exe was not found. Install Git for Windows to preview repositories.");
                context.IsBusy = false;
                return;
            }

            _cts = new CancellationTokenSource();
            _runner = new GitCommandRunner(location.StartDirectory);

            var runner = _runner;
            var token = _cts.Token;
            var reader = new GitRepositoryReader(runner);

            // 展开某条提交时才去读它的文件改动。必须在 ApplyDetails 之前装好，
            // 因为提交行是在那里包出来的，装配晚了它们就拿不到加载器。
            panel.Model.FilesLoader = commit => Task.Run(() => LoadFiles(commit, panel, reader, token));

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

            if (_panel != null)
            {
                _panel.Cleanup();
                _panel = null;
            }
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
                    panel.Model.ApplyOverview(location, overview);
                    context.IsBusy = false;
                });

                var details = reader.ReadDetails(ct);
                if (ct.IsCancellationRequested)
                    return;

                Marshal(panel, ct, () => panel.Model.ApplyDetails(details));
            }
            catch (Exception e)
            {
                ProcessHelper.WriteLog(string.Format(CultureInfo.InvariantCulture,
                    "GitViewer: failed to read {0}: {1}", location.StartDirectory, e));

                Marshal(panel, ct, () =>
                {
                    panel.Model.ErrorMessage = Translate.Get("ErrorReadFailed", "Could not read this repository.")
                                               + Environment.NewLine + e.Message;
                    context.IsBusy = false;
                });
            }
        }

        /// <summary>
        ///     读取单条提交的文件改动。展开提交行时由视图模型经 FilesLoader 触发。
        ///     <para>
        ///     预览关掉之后再走到这里是安全的：GitCommandRunner 一旦 Dispose，
        ///     Run 会直接返回失败，而 Marshal 又会在派发前后各查一次取消标记。
        ///     </para>
        /// </summary>
        private static void LoadFiles(CommitViewModel commit, GitPanel panel, GitRepositoryReader reader,
            CancellationToken ct)
        {
            // 和 Load 一样，异常绝不许逃出后台任务。
            try
            {
                var files = reader.ReadCommitFiles(commit.Hash, ct);
                if (ct.IsCancellationRequested)
                    return;

                // null 表示 git 调用本身失败；空列表表示这个提交确实没有改动。
                if (files == null)
                    Marshal(panel, ct, commit.ApplyLoadFailure);
                else
                    Marshal(panel, ct, () => commit.ApplyFiles(files));
            }
            catch (Exception e)
            {
                ProcessHelper.WriteLog(string.Format(CultureInfo.InvariantCulture,
                    "GitViewer: failed to read files of {0}: {1}", commit.Hash, e));

                Marshal(panel, ct, commit.ApplyLoadFailure);
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
