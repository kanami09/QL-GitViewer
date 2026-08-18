using QuickLook.Common.Helpers;
using QuickLook.Plugin.GitViewer.Git;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace QuickLook.Plugin.GitViewer
{
    /// <summary>
    ///     预览面板本体。所有改动都发生在 UI 线程上；插件负责把后台加载的结果
    ///     切换到这个线程再调进来。
    /// </summary>
    public partial class GitPanel : UserControl
    {
        private readonly GitPanelViewModel _model = new GitPanelViewModel();
        private DispatcherTimer _toastTimer;

        public GitPanel()
        {
            InitializeComponent();
            ApplyTheme();
            DataContext = _model;
        }

        /// <summary>
        ///     合并浅色或深色画刷。这个 URI 必须写成带程序集名的完整形式：插件是用
        ///     Assembly.LoadFrom 加载的，相对 pack URI 会去 QuickLook.exe 里找资源，
        ///     而不是本程序集。
        /// </summary>
        private void ApplyTheme()
        {
            var theme = OSThemeHelper.AppsUseDarkTheme() ? "Dark" : "Light";
            var uri = new Uri(
                "pack://application:,,,/QuickLook.Plugin.GitViewer;component/Themes/" + theme + ".xaml",
                UriKind.Absolute);

            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
        }

        /// <summary>填充头部概览。加载的第一阶段一返回就会调用。</summary>
        public void ApplyOverview(RepositoryLocation location, GitRepositoryInfo info)
        {
            _model.RepositoryName = location.DisplayName;
            _model.RepositoryPath = FirstNonEmpty(info.WorkTree, info.GitDir, location.StartDirectory);

            _model.BranchLabel = !string.IsNullOrEmpty(info.BranchName)
                ? info.BranchName
                : Translate.Get("HeadLabel", "HEAD");

            _model.ShortHash = info.ShortHash;
            _model.BadgeText = BuildBadge(info);

            // 找不到可达的标签时，"describe --always" 会退化成缩写哈希，
            // 那样就和旁边的哈希标签重复了。
            _model.Describe = !string.IsNullOrEmpty(info.Describe) && info.Describe != info.ShortHash
                ? info.Describe
                : null;

            if (info.HasUpstream)
            {
                _model.UpstreamText = info.Upstream;
                _model.SyncText = BuildSyncText(info);
            }

            if (info.StashCount > 0)
                _model.StashText = string.Format(
                    Translate.Get(info.StashCount == 1 ? "StashOne" : "StashMany",
                        info.StashCount == 1 ? "{0} stash" : "{0} stashes"),
                    info.StashCount);
        }

        /// <summary>填充各个页签。加载的第二阶段返回时调用。</summary>
        public void ApplyDetails(GitRepositoryDetails details)
        {
            _model.Commits = details.Commits;
            _model.SetBranches(details.LocalBranches, details.RemoteBranches);
            _model.Tags = details.Tags;
            _model.Remotes = details.Remotes;
        }

        /// <summary>用一条消息取代页签区域。git 缺失或读取失败时使用。</summary>
        public void ShowError(string message)
        {
            _model.ErrorMessage = message;
        }

        /// <summary>完全无法读取仓库时，头部退而求其次显示的内容。</summary>
        public void ShowFallbackHeader(RepositoryLocation location)
        {
            _model.RepositoryName = location.DisplayName;
            _model.RepositoryPath = location.StartDirectory;
            _model.BranchLabel = Translate.Get("Unknown", "unknown");
        }

        private static string BuildBadge(GitRepositoryInfo info)
        {
            var parts = new List<string>();

            if (info.IsBare)
                parts.Add(Translate.Get("BadgeBare", "bare"));
            if (info.IsEmpty)
                parts.Add(Translate.Get("BadgeEmpty", "no commits"));
            if (info.IsDetached)
                parts.Add(Translate.Get("BadgeDetached", "detached HEAD"));

            return parts.Count == 0 ? null : string.Join(" - ", parts.ToArray());
        }

        private static string BuildSyncText(GitRepositoryInfo info)
        {
            if (info.Ahead == 0 && info.Behind == 0)
                return Translate.Get("SyncUpToDate", "up to date");

            var parts = new List<string>();

            if (info.Ahead > 0)
                parts.Add(string.Format(Translate.Get("SyncAhead", "{0} ahead"), info.Ahead));
            if (info.Behind > 0)
                parts.Add(string.Format(Translate.Get("SyncBehind", "{0} behind"), info.Behind));

            return string.Join(", ", parts.ToArray());
        }

        private static string FirstNonEmpty(params string[] candidates)
        {
            foreach (var candidate in candidates)
                if (!string.IsNullOrEmpty(candidate))
                    return candidate;

            return string.Empty;
        }

        // WPF 右键点击不会选中行，不做这一步的话右键菜单操作的就会是
        // 上一次左键点中的那一行。
        private void OnRowRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = sender as ListBoxItem;
            if (item != null)
                item.IsSelected = true;
        }

        private void OnCopyCommitHash(object sender, RoutedEventArgs e)
        {
            var commit = GetSelectedItem(sender) as GitCommitInfo;
            if (commit != null)
                CopyToClipboard(commit.Hash);
        }

        private void OnCopyRefName(object sender, RoutedEventArgs e)
        {
            var reference = GetSelectedItem(sender) as GitRefInfo;
            if (reference != null)
                CopyToClipboard(reference.Name);
        }

        private void OnCopyRemoteUrl(object sender, RoutedEventArgs e)
        {
            var remote = GetSelectedItem(sender) as GitRemoteInfo;
            if (remote != null)
                CopyToClipboard(remote.Url);
        }

        /// <summary>从被点击的菜单项回溯到它所属的那个列表。</summary>
        private static object GetSelectedItem(object sender)
        {
            var menuItem = sender as MenuItem;
            if (menuItem == null)
                return null;

            var menu = menuItem.Parent as ContextMenu;
            if (menu == null)
                return null;

            var list = menu.PlacementTarget as ListBox;
            return list == null ? null : list.SelectedItem;
        }

        /// <summary>
        ///     写入剪贴板并给出可见反馈。走 <see cref="ClipboardHelper" /> 的 Win32 路径，
        ///     最坏阻塞约 100ms；WPF 的 Clipboard 一次失败就要阻塞约 1 秒，不能在这里用。
        /// </summary>
        private void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var copied = ClipboardHelper.SetText(text, GetWindowHandle());

            ShowToast(copied
                ? Translate.Get("CopyDone", "Copied")
                : Translate.Get("CopyFailed", "Copy failed: the clipboard is in use"));
        }

        /// <summary>
        ///     取本控件所在窗口的句柄，用作剪贴板属主。
        ///     控件还没挂到窗口上时返回 IntPtr.Zero。
        /// </summary>
        private IntPtr GetWindowHandle()
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            return source == null ? IntPtr.Zero : source.Handle;
        }

        /// <summary>在面板底部显示一条短暂提示，几秒后自动消失。</summary>
        private void ShowToast(string message)
        {
            _model.ToastText = message;

            if (_toastTimer == null)
            {
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                _toastTimer.Tick += (sender, e) =>
                {
                    _toastTimer.Stop();
                    _model.ToastText = null;
                };
            }

            // 重新计时，连续复制时提示不会提前消失。
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        /// <summary>由 IViewer.Cleanup 调用，停掉计时器以免它继续持有本控件。</summary>
        public void Cleanup()
        {
            if (_toastTimer == null)
                return;

            _toastTimer.Stop();
            _toastTimer = null;
        }
    }
}
