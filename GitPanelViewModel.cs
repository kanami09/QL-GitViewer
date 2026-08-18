using QuickLook.Plugin.GitViewer.Git;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickLook.Plugin.GitViewer
{
    /// <summary>
    ///     <see cref="GitPanel" /> 的数据源。分两步在 UI 线程上填充：
    ///     先填头部概览，等明细查询返回后再填各个页签的内容。
    /// </summary>
    public sealed class GitPanelViewModel : INotifyPropertyChanged
    {
        private string _badgeText;
        private string _branchLabel;
        private IList<GitCommitInfo> _commits = new List<GitCommitInfo>();
        private string _describe;
        private string _errorMessage;
        private IList<GitRefInfo> _localBranches = new List<GitRefInfo>();
        private string _remoteBranchesHeader;
        private IList<GitRefInfo> _remoteBranches = new List<GitRefInfo>();
        private IList<GitRemoteInfo> _remotes = new List<GitRemoteInfo>();
        private string _repositoryName;
        private string _repositoryPath;
        private string _shortHash;
        private string _stashText;
        private string _syncText;
        private IList<GitRefInfo> _tags = new List<GitRefInfo>();
        private string _upstreamText;

        public GitPanelViewModel()
        {
            CommitsHeader = Translate.Get("TabCommits", "Commits");
            BranchesHeader = Translate.Get("TabBranches", "Branches");
            TagsHeader = Translate.Get("TabTags", "Tags");
            RemotesHeader = Translate.Get("TabRemotes", "Remotes");

            LocalBranchesHeader = Translate.Get("SectionLocalBranches", "Local");
            RemoteBranchesHeader = Translate.Get("SectionRemoteBranches", "Remote-tracking");

            EmptyCommitsText = Translate.Get("EmptyCommits", "No commits yet.");
            EmptyBranchesText = Translate.Get("EmptyBranches", "No branches.");
            EmptyTagsText = Translate.Get("EmptyTags", "No tags.");
            EmptyRemotesText = Translate.Get("EmptyRemotes", "No remotes configured.");

            CopyHashText = Translate.Get("MenuCopyHash", "Copy commit hash");
            CopyNameText = Translate.Get("MenuCopyName", "Copy name");
            CopyUrlText = Translate.Get("MenuCopyUrl", "Copy URL");
        }

        // 固定文案，构造时解析一次即可。
        public string CommitsHeader { get; private set; }
        public string BranchesHeader { get; private set; }
        public string TagsHeader { get; private set; }
        public string RemotesHeader { get; private set; }
        public string LocalBranchesHeader { get; private set; }
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

        public IList<GitCommitInfo> Commits
        {
            get { return _commits; }
            set
            {
                Set(ref _commits, value);
                OnPropertyChanged("HasCommits");
            }
        }

        public bool HasCommits => _commits != null && _commits.Count > 0;

        public IList<GitRefInfo> LocalBranches
        {
            get { return _localBranches; }
            set
            {
                Set(ref _localBranches, value);
                OnPropertyChanged("HasLocalBranches");
                OnPropertyChanged("HasAnyBranch");
            }
        }

        public bool HasLocalBranches => _localBranches != null && _localBranches.Count > 0;

        public IList<GitRefInfo> RemoteBranches
        {
            get { return _remoteBranches; }
            set
            {
                Set(ref _remoteBranches, value);
                OnPropertyChanged("HasRemoteBranches");
                OnPropertyChanged("HasAnyBranch");
            }
        }

        public bool HasRemoteBranches => _remoteBranches != null && _remoteBranches.Count > 0;

        public bool HasAnyBranch => HasLocalBranches || HasRemoteBranches;

        public string RemoteBranchesHeader
        {
            get { return _remoteBranchesHeader; }
            set { Set(ref _remoteBranchesHeader, value); }
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

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            field = value;
            OnPropertyChanged(propertyName);
        }

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
