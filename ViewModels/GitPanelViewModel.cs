using QuickLook.Plugin.GitViewer.Git;
using QuickLook.Plugin.GitViewer.Helpers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace QuickLook.Plugin.GitViewer.ViewModels
{
    /// <summary>
    ///     请求读取下一页提交。由 <see cref="Plugin" /> 装配，实现负责在后台读流并把结果
    ///     切回 UI 线程调 <see cref="GitPanelViewModel.AppendCommits" /> 或
    ///     <see cref="GitPanelViewModel.ApplyCommitsLoadFailure" />。
    /// </summary>
    public delegate void CommitPageLoader();

    /// <summary>
    ///     <see cref="GitPanel" /> 的数据源。分两步在 UI 线程上填充：
    ///     先填头部概览，等明细查询返回后再填各个页签的内容。
    /// </summary>
    public sealed class GitPanelViewModel : ObservableObject
    {
        private string _badgeText;
        private string _branchLabel;
        private readonly ObservableCollection<CommitViewModel> _commits = new ObservableCollection<CommitViewModel>();
        private bool _hasMoreCommits = true;
        private bool _isLoadingCommits;
        private bool _commitsLoadFailed;
        private string _describe;
        private readonly string _loadingMoreCommitsText;
        private readonly string _allCommitsLoadedOneText;
        private readonly string _allCommitsLoadedText;
        private readonly string _commitsLoadFailedText;
        private string _errorMessage;
        private ICollectionView _branchesView;
        private int _branchCount;
        private IList<GitRemoteInfo> _remotes = new List<GitRemoteInfo>();
        private string _repositoryName;
        private string _repositoryPath;
        private string _shortHash;
        private string _stashText;
        private string _syncText;
        private string _toastText;
        private IList<GitRefInfo> _tags = new List<GitRefInfo>();
        private string _upstreamText;

        public GitPanelViewModel()
        {
            CommitsHeader = Translate.Get("TabCommits", "Commits");
            BranchesHeader = Translate.Get("TabBranches", "Branches");
            TagsHeader = Translate.Get("TabTags", "Tags");
            RemotesHeader = Translate.Get("TabRemotes", "Remotes");

            EmptyCommitsText = Translate.Get("EmptyCommits", "No commits yet.");
            EmptyBranchesText = Translate.Get("EmptyBranches", "No branches.");
            EmptyTagsText = Translate.Get("EmptyTags", "No tags.");
            EmptyRemotesText = Translate.Get("EmptyRemotes", "No remotes configured.");

            CopyHashText = Translate.Get("MenuCopyHash", "Copy commit hash");
            CopyNameText = Translate.Get("MenuCopyName", "Copy name");
            CopyUrlText = Translate.Get("MenuCopyUrl", "Copy URL");

            _loadingMoreCommitsText = Translate.Get("CommitsLoadingMore", "Loading more commits…");
            _allCommitsLoadedOneText = Translate.Get("CommitsAllLoadedOne", "{0} commit in total");
            _allCommitsLoadedText = Translate.Get("CommitsAllLoaded", "{0} commits in total");
            _commitsLoadFailedText = Translate.Get("CommitsLoadMoreFailed", "Could not read more commits.");
        }

        // 固定文案，构造时解析一次即可。
        public string CommitsHeader { get; private set; }
        public string BranchesHeader { get; private set; }
        public string TagsHeader { get; private set; }
        public string RemotesHeader { get; private set; }
        public string EmptyCommitsText { get; private set; }
        public string EmptyBranchesText { get; private set; }
        public string EmptyTagsText { get; private set; }
        public string EmptyRemotesText { get; private set; }
        public string CopyHashText { get; private set; }
        public string CopyNameText { get; private set; }
        public string CopyUrlText { get; private set; }

        public string RepositoryName
        {
            get { return _repositoryName; }
            set { Set(ref _repositoryName, value); }
        }

        public string RepositoryPath
        {
            get { return _repositoryPath; }
            set { Set(ref _repositoryPath, value); }
        }

        /// <summary>分支名；HEAD 分离时显示 "HEAD"。</summary>
        public string BranchLabel
        {
            get { return _branchLabel; }
            set { Set(ref _branchLabel, value); }
        }

        public string ShortHash
        {
            get { return _shortHash; }
            set
            {
                Set(ref _shortHash, value);
                OnPropertyChanged("HasShortHash");
            }
        }

        public bool HasShortHash => !string.IsNullOrEmpty(_shortHash);

        public string Describe
        {
            get { return _describe; }
            set
            {
                Set(ref _describe, value);
                OnPropertyChanged("HasDescribe");
            }
        }

        public bool HasDescribe => !string.IsNullOrEmpty(_describe);

        /// <summary>"裸仓库"、"无提交"、"分离 HEAD" 中适用的那些，都不适用时为 null。</summary>
        public string BadgeText
        {
            get { return _badgeText; }
            set
            {
                Set(ref _badgeText, value);
                OnPropertyChanged("HasBadge");
            }
        }

        public bool HasBadge => !string.IsNullOrEmpty(_badgeText);

        public string UpstreamText
        {
            get { return _upstreamText; }
            set
            {
                Set(ref _upstreamText, value);
                OnPropertyChanged("HasUpstream");
            }
        }

        public bool HasUpstream => !string.IsNullOrEmpty(_upstreamText);

        /// <summary>相对上游的领先/落后摘要，例如 "领先 2, 落后 1"。</summary>
        public string SyncText
        {
            get { return _syncText; }
            set
            {
                Set(ref _syncText, value);
                OnPropertyChanged("HasSync");
            }
        }

        public bool HasSync => !string.IsNullOrEmpty(_syncText);

        public string StashText
        {
            get { return _stashText; }
            set
            {
                Set(ref _stashText, value);
                OnPropertyChanged("HasStash");
            }
        }

        public bool HasStash => !string.IsNullOrEmpty(_stashText);

        public string ErrorMessage
        {
            get { return _errorMessage; }
            set
            {
                Set(ref _errorMessage, value);
                OnPropertyChanged("HasError");
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        /// <summary>复制后在面板底部一闪而过的提示文字。</summary>
        public string ToastText
        {
            get { return _toastText; }
            set
            {
                Set(ref _toastText, value);
                OnPropertyChanged("HasToast");
            }
        }

        public bool HasToast => !string.IsNullOrEmpty(_toastText);

        /// <summary>
        ///     提交列表。集合实例从头到尾只有一个：一页页读进来的提交是追加进去的，
        ///     换实例会让列表整个重建，已经展开的行会收起来，滚动位置也会跳回顶部。
        /// </summary>
        public ObservableCollection<CommitViewModel> Commits => _commits;

        public bool HasCommits => _commits.Count > 0;

        /// <summary>还没读到最初的那条提交。</summary>
        public bool HasMoreCommits => _hasMoreCommits;

        /// <summary>提交列表下方的状态行，没什么可说的时候为 null。</summary>
        public string CommitsStatusText
        {
            get
            {
                if (_commitsLoadFailed)
                    return _commitsLoadFailedText;

                if (_isLoadingCommits)
                    return _loadingMoreCommitsText;

                // 全读完了才敢报总数 —— 中途报的是"已加载多少"，不是"一共多少"。
                if (!_hasMoreCommits && _commits.Count > 0)
                    return string.Format(CultureInfo.CurrentCulture,
                        _commits.Count == 1 ? _allCommitsLoadedOneText : _allCommitsLoadedText,
                        _commits.Count);

                return null;
            }
        }

        public bool HasCommitsStatus => !string.IsNullOrEmpty(CommitsStatusText);

        /// <summary>
        ///     请求读取下一页提交。列表滚到接近底部时由 <see cref="GitPanel" /> 调用，
        ///     滚动事件会连着来好几发，所以这里必须自己去重。
        ///     <para>
        ///     "同一时刻只有一次读取在跑"也是流那边的要求：<see cref="GitRecordStream" />
        ///     不是线程安全的。
        ///     </para>
        /// </summary>
        public void RequestMoreCommits()
        {
            if (_isLoadingCommits || !_hasMoreCommits || _commitsLoadFailed || MoreCommitsLoader == null)
                return;

            _isLoadingCommits = true;
            NotifyCommitsStatus();

            MoreCommitsLoader();
        }

        /// <summary>
        ///     追加一页提交。必须在 UI 线程上调用。
        /// </summary>
        /// <param name="commits">这一页读到的提交，可以为空。</param>
        /// <param name="finished">是否已经读到最初的那条提交。</param>
        public void AppendCommits(IList<GitCommitInfo> commits, bool finished)
        {
            var wasEmpty = _commits.Count == 0;

            if (commits != null)
                foreach (var commit in commits)
                    _commits.Add(new CommitViewModel(commit, FilesLoader));

            _isLoadingCommits = false;
            _hasMoreCommits = !finished;

            if (wasEmpty != (_commits.Count == 0))
                OnPropertyChanged("HasCommits");

            OnPropertyChanged("HasMoreCommits");
            NotifyCommitsStatus();
        }

        /// <summary>读取提交失败。必须在 UI 线程上调用。之后不再尝试，免得滚动一次重试一次。</summary>
        public void ApplyCommitsLoadFailure()
        {
            _isLoadingCommits = false;
            _hasMoreCommits = false;
            _commitsLoadFailed = true;

            OnPropertyChanged("HasMoreCommits");
            NotifyCommitsStatus();
        }

        private void NotifyCommitsStatus()
        {
            OnPropertyChanged("CommitsStatusText");
            OnPropertyChanged("HasCommitsStatus");
        }

        /// <summary>
        ///     本地与远程分支合并后的分组视图。用单个列表加分组，
        ///     而不是两个独立列表 —— 后者每个都有自己的 SelectedItem，
        ///     点完一边再点另一边会变成两行同时高亮。
        /// </summary>
        public ICollectionView BranchesView
        {
            get { return _branchesView; }
            private set { Set(ref _branchesView, value); }
        }

        public bool HasAnyBranch => _branchCount > 0;

        /// <summary>把两组分支合成一个按 GroupLabel 分组的视图。</summary>
        public void SetBranches(IList<GitRefInfo> local, IList<GitRefInfo> remote)
        {
            var all = new List<GitRefInfo>();
            if (local != null) all.AddRange(local);
            if (remote != null) all.AddRange(remote);

            // 分组标题在这里贴上去，模型自己不认识界面文案。
            var localLabel = Translate.Get("SectionLocalBranches", "Local");
            var remoteLabel = Translate.Get("SectionRemoteBranches", "Remote-tracking");

            foreach (var item in all)
                item.GroupLabel = item.Kind == GitRefKind.RemoteBranch ? remoteLabel : localLabel;

            _branchCount = all.Count;

            var view = new ListCollectionView(all);
            view.GroupDescriptions.Add(new PropertyGroupDescription("GroupLabel"));

            BranchesView = view;
            OnPropertyChanged("HasAnyBranch");
        }

        public IList<GitRefInfo> Tags
        {
            get { return _tags; }
            set
            {
                Set(ref _tags, value);
                OnPropertyChanged("HasTags");
            }
        }

        public bool HasTags => _tags != null && _tags.Count > 0;

        public IList<GitRemoteInfo> Remotes
        {
            get { return _remotes; }
            set
            {
                Set(ref _remotes, value);
                OnPropertyChanged("HasRemotes");
            }
        }

        public bool HasRemotes => _remotes != null && _remotes.Count > 0;

        /// <summary>填充头部概览。加载的第一阶段一返回就会调用。</summary>
        public void ApplyOverview(RepositoryLocation location, GitRepositoryInfo info)
        {
            RepositoryName = location.DisplayName;
            RepositoryPath = FirstNonEmpty(info.WorkTree, info.GitDir, location.StartDirectory);

            BranchLabel = !string.IsNullOrEmpty(info.BranchName)
                ? info.BranchName
                : Translate.Get("HeadLabel", "HEAD");

            ShortHash = info.ShortHash;
            BadgeText = BuildBadge(info);

            // 找不到可达的标签时，"describe --always" 会退化成缩写哈希，
            // 那样就和旁边的哈希标签重复了。
            Describe = !string.IsNullOrEmpty(info.Describe) && info.Describe != info.ShortHash
                ? info.Describe
                : null;

            if (info.HasUpstream)
            {
                UpstreamText = info.Upstream;
                SyncText = BuildSyncText(info);
            }

            if (info.StashCount > 0)
                StashText = string.Format(
                    Translate.Get(info.StashCount == 1 ? "StashOne" : "StashMany",
                        info.StashCount == 1 ? "{0} stash" : "{0} stashes"),
                    info.StashCount);
        }

        /// <summary>
        ///     展开某条提交时用来惰性读取文件改动。由 <see cref="Plugin" /> 在创建
        ///     runner 之后装配，必须早于 <see cref="AppendCommits" />。
        /// </summary>
        public CommitFilesLoader FilesLoader { get; set; }

        /// <summary>
        ///     读取下一页提交。由 <see cref="Plugin" /> 在起好提交流之后装配，
        ///     必须早于第一次 <see cref="AppendCommits" /> —— 否则第一页画出来时
        ///     列表已经在往下要第二页了，却找不到人去读。
        /// </summary>
        public CommitPageLoader MoreCommitsLoader { get; set; }

        /// <summary>填充分支、标签和远程页签。加载的第二阶段返回时调用。</summary>
        public void ApplyDetails(GitRepositoryDetails details)
        {
            SetBranches(details.LocalBranches, details.RemoteBranches);
            Tags = details.Tags;
            Remotes = details.Remotes;
        }

        /// <summary>完全无法读取仓库时，头部退而求其次显示的内容。</summary>
        public void ApplyFallbackHeader(RepositoryLocation location)
        {
            RepositoryName = location.DisplayName;
            RepositoryPath = location.StartDirectory;
            BranchLabel = Translate.Get("Unknown", "unknown");
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

    }
}
