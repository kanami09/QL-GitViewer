using QuickLook.Common.Helpers;
using QuickLook.Plugin.GitViewer.Git;
using QuickLook.Plugin.GitViewer.Helpers;
using System;
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

        /// <summary>
        ///     供 <see cref="Plugin" /> 在后台加载结束后回填数据。填充逻辑全部在
        ///     视图模型里，这里不再做转发。
        /// </summary>
        public GitPanelViewModel Model
        {
            get { return _model; }
        }

        // WPF 右键点击不会选中行，不做这一步的话右键菜单操作的就会是
        // 上一次左键点中的那一行。
        // 挂在 ListBox 上而不是行容器的样式上：EventSetter 的 Handler 要求所在的
        // ResourceDictionary 带 x:Class code-behind，而行样式已经搬进
        // Themes/Controls.xaml，那是个纯资源文件。
        private void OnRowRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var list = sender as ListBox;
            var source = e.OriginalSource as DependencyObject;
            if (list == null || source == null)
                return;

            // 点在列表空白处时找不到行容器，返回 null，此时保持原有选中不变。
            var item = ItemsControl.ContainerFromElement(list, source) as ListBoxItem;
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
