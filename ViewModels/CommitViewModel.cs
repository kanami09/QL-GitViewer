using QuickLook.Plugin.GitViewer.Git;
using QuickLook.Plugin.GitViewer.Helpers;
using System;
using System.Collections.Generic;

namespace QuickLook.Plugin.GitViewer.ViewModels
{
    /// <summary>
    ///     请求读取某条提交的文件改动。由 <see cref="Plugin" /> 装配，
    ///     实现负责在后台跑 git 并把结果切回 UI 线程调
    ///     <see cref="CommitViewModel.ApplyFiles" /> 或
    ///     <see cref="CommitViewModel.ApplyLoadFailure" />。
    /// </summary>
    public delegate void CommitFilesLoader(CommitViewModel commit);

    /// <summary>
    ///     提交列表里的一行。除了显示用的派生属性，还持有"是否展开"这个可变状态 ——
    ///     状态必须存在数据项上而不是容器上，否则列表虚拟化回收容器时展开状态会串行。
    /// </summary>
    public sealed class CommitViewModel : ObservableObject
    {
        private static readonly string LoadingText = Translate.Get("DetailLoading", "Loading changes…");
        private static readonly string NoChangesText = Translate.Get("DetailNoChanges", "No file changes.");
        private static readonly string MergeText = Translate.Get("DetailMergeCommit",
            "Merge commit - file changes are not listed.");
        private static readonly string LoadFailedText = Translate.Get("DetailLoadFailed",
            "Could not read the changes for this commit.");

        private static readonly string StatusAddedText = Translate.Get("StatusAdded", "Added");
        private static readonly string StatusModifiedText = Translate.Get("StatusModified", "Modified");
        private static readonly string StatusDeletedText = Translate.Get("StatusDeleted", "Deleted");
        private static readonly string StatusRenamedText = Translate.Get("StatusRenamed", "Renamed");
        private static readonly string StatusCopiedText = Translate.Get("StatusCopied", "Copied");
        private static readonly string StatusTypeChangedText = Translate.Get("StatusTypeChanged", "Type changed");
        private static readonly string StatusUnknownText = Translate.Get("StatusUnknown", "Unknown");
        private static readonly string StatusSimilarityFormat = Translate.Get("StatusSimilarity",
            "{0} ({1}% similar)");

        private readonly GitCommitInfo _info;
        private readonly CommitFilesLoader _loader;

        private IList<GitFileChange> _files = new List<GitFileChange>();
        private bool _filesRequested;
        private bool _isExpanded;
        private bool _isLoadingFiles;
        private bool _loadFailed;
        private bool _filesLoaded;

        public CommitViewModel(GitCommitInfo info, CommitFilesLoader loader)
        {
            _info = info;
            _loader = loader;
        }

        public string Hash => _info.Hash;
        public string ShortHash => _info.ShortHash;
        public string AuthorName => _info.AuthorName;
        public string Subject => _info.Subject;
        public string Body => _info.Body;
        public bool HasBody => !string.IsNullOrEmpty(_info.Body);

        public string RelativeTime => TimeFormat.Relative(_info.When);

        public string AbsoluteTime => _info.When.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");

        public string AuthorTooltip => string.IsNullOrEmpty(_info.AuthorEmail)
            ? _info.AuthorName
            : _info.AuthorName + " <" + _info.AuthorEmail + ">";

        public bool HasDecorations => !string.IsNullOrEmpty(_info.Decorations);

        /// <summary>把装饰拆成一个个小标签，例如 "HEAD -&gt; master"、"tag: v1"。</summary>
        public string[] DecorationList => string.IsNullOrEmpty(_info.Decorations)
            ? new string[0]
            : _info.Decorations.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>合并提交。git show 对它默认不输出任何 diff，所以不去请求文件列表。</summary>
        public bool IsMerge => _info.ParentCount > 1;

        /// <summary>
        ///     行是否展开。首次展开时触发一次文件列表加载；无论成败都不再重试，
        ///     免得反复折叠展开就反复拉起 git 进程。
        /// </summary>
        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (!Set(ref _isExpanded, value))
                    return;

                if (value)
                    RequestFiles();
            }
        }

        public IList<GitFileChange> Files
        {
            get { return _files; }
            private set
            {
                Set(ref _files, value);
                OnPropertyChanged("HasFiles");
            }
        }

        public bool HasFiles => _files != null && _files.Count > 0;

        public bool IsLoadingFiles
        {
            get { return _isLoadingFiles; }
            private set { Set(ref _isLoadingFiles, value); }
        }

        public bool LoadFailed
        {
            get { return _loadFailed; }
            private set { Set(ref _loadFailed, value); }
        }

        /// <summary>读取成功但一个文件都没有 —— 空提交，或者被 git 判定为无 diff。</summary>
        public bool HasNoChanges => _filesLoaded && !HasFiles;

        public string LoadingLabel => LoadingText;
        public string NoChangesLabel => NoChangesText;
        public string MergeLabel => MergeText;
        public string LoadFailedLabel => LoadFailedText;

        private void RequestFiles()
        {
            if (_filesRequested || IsMerge || _loader == null)
                return;

            _filesRequested = true;
            IsLoadingFiles = true;
            _loader(this);
        }

        /// <summary>加载成功。必须在 UI 线程上调用。</summary>
        public void ApplyFiles(IList<GitFileChange> files)
        {
            IsLoadingFiles = false;
            _filesLoaded = true;

            var list = files ?? new List<GitFileChange>();
            foreach (var change in list)
                change.StatusTooltip = DescribeStatus(change);

            Files = list;
            OnPropertyChanged("HasNoChanges");
        }

        /// <summary>
        ///     把状态字母翻成人话。重命名和复制带上相似度，
        ///     比如"重命名（95% 相似）"—— 原始的 "R095" 对不熟这套字母的人等于没说。
        /// </summary>
        private static string DescribeStatus(GitFileChange change)
        {
            string name;

            switch (change.Kind)
            {
                case GitFileChangeKind.Added: name = StatusAddedText; break;
                case GitFileChangeKind.Modified: name = StatusModifiedText; break;
                case GitFileChangeKind.Deleted: name = StatusDeletedText; break;
                case GitFileChangeKind.Renamed: name = StatusRenamedText; break;
                case GitFileChangeKind.Copied: name = StatusCopiedText; break;
                case GitFileChangeKind.TypeChanged: name = StatusTypeChangedText; break;
                default: name = StatusUnknownText; break;
            }

            return change.SimilarityPercent > 0
                ? string.Format(StatusSimilarityFormat, name, change.SimilarityPercent)
                : name;
        }

        /// <summary>加载失败。必须在 UI 线程上调用。</summary>
        public void ApplyLoadFailure()
        {
            IsLoadingFiles = false;
            LoadFailed = true;
        }
    }
}
