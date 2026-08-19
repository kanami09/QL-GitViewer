using QuickLook.Plugin.GitViewer.Helpers;
using System;
using System.Collections.Generic;

namespace QuickLook.Plugin.GitViewer.Git
{
    /// <summary>
    ///     仓库在磁盘上的位置，仅通过文件系统判断得出。
    ///     由 <see cref="GitRepositoryLocator" /> 产出，而它运行在 <c>IViewer.CanHandle</c> 里面，
    ///     所以整个判断过程绝不能启动进程。
    /// </summary>
    public sealed class RepositoryLocation
    {
        /// <summary>git.exe 的启动目录；由 git 自己去发现仓库。</summary>
        public string StartDirectory { get; set; }

        /// <summary>显示在预览窗口标题栏上的名字（工作区文件夹名）。</summary>
        public string DisplayName { get; set; }

        /// <summary>用户按空格的是 <c>.git</c> 文件夹而不是仓库根目录时为 true。</summary>
        public bool SelectedGitDir { get; set; }
    }

    /// <summary>单次 git.exe 调用的结果。退出码非 0 本身并不代表出错。</summary>
    public sealed class GitResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = string.Empty;
        public string StdErr { get; set; } = string.Empty;

        public bool Success => ExitCode == 0;

        /// <summary>去掉行尾换行的 StdOut；命令失败时返回空串。</summary>
        public string Line => Success ? StdOut.TrimEnd('\r', '\n') : string.Empty;
    }

    /// <summary>仓库概览，由加载的第一阶段填充。</summary>
    public sealed class GitRepositoryInfo
    {
        public string GitDir { get; set; }
        public string WorkTree { get; set; }
        public bool IsBare { get; set; }

        /// <summary>HEAD 尚未诞生（unborn）时为 true，即仓库还一个提交都没有。</summary>
        public bool IsEmpty { get; set; }

        public bool IsDetached { get; set; }
        public string BranchName { get; set; }
        public string ShortHash { get; set; }
        public string Describe { get; set; }

        public bool HasUpstream { get; set; }
        public string Upstream { get; set; }
        public int Ahead { get; set; }
        public int Behind { get; set; }

        public int StashCount { get; set; }
    }

    /// <summary>
    ///     提交历史里的一条记录。纯数据 —— 显示用的派生属性都在 CommitViewModel 上。
    /// </summary>
    public sealed class GitCommitInfo
    {
        public string Hash { get; set; }
        public string ShortHash { get; set; }
        public string AuthorName { get; set; }
        public string AuthorEmail { get; set; }
        public DateTimeOffset When { get; set; }

        /// <summary>该提交上的 ref 装饰（git 的 %D），例如 "HEAD -&gt; master, origin/master, tag: v1"。</summary>
        public string Decorations { get; set; }

        public string Subject { get; set; }

        /// <summary>标题之后的说明正文（git 的 %b），可以是多行，没有正文时为空串。</summary>
        public string Body { get; set; }

        /// <summary>父提交个数（由 %P 数出来）。大于 1 即合并提交。</summary>
        public int ParentCount { get; set; }
    }

    /// <summary>一次提交里某个文件的改动类型。</summary>
    public enum GitFileChangeKind
    {
        Added,
        Modified,
        Deleted,
        Renamed,
        Copied,
        TypeChanged,
        Unknown
    }

    /// <summary>
    ///     一次提交里的一个文件改动。状态与路径取自 <c>--raw</c> 输出，
    ///     增删行数取自 <c>--numstat</c> 输出。
    /// </summary>
    public sealed class GitFileChange
    {
        public GitFileChangeKind Kind { get; set; }

        /// <summary>raw 输出里的原始状态字段，例如 "M"、"R095"。</summary>
        public string Status { get; set; }

        public string Path { get; set; }

        /// <summary>重命名或复制时的原路径，其余情况为 null。</summary>
        public string OldPath { get; set; }

        public bool HasOldPath => !string.IsNullOrEmpty(OldPath);

        public int Added { get; set; }
        public int Deleted { get; set; }

        /// <summary>二进制文件；numstat 对它输出 "-" 而不是行数。</summary>
        public bool IsBinary { get; set; }
    }

    public enum GitRefKind
    {
        LocalBranch,
        RemoteBranch,
        Tag
    }

    /// <summary>一个分支或标签，来自 <c>git for-each-ref</c> 的输出。</summary>
    public sealed class GitRefInfo
    {
        public GitRefKind Kind { get; set; }

        /// <summary>短名称，例如 "master"、"origin/master"、"v1.0"。</summary>
        public string Name { get; set; }

        public string ObjectName { get; set; }
        public DateTimeOffset When { get; set; }
        public string Upstream { get; set; }

        /// <summary>git 的 %(upstream:track)，例如 "[ahead 3, behind 1]"。已同步或没有跟踪时为空。</summary>
        public string Track { get; set; }

        public bool IsHead { get; set; }
        public string AuthorName { get; set; }
        public string Subject { get; set; }

        public string RelativeTime => TimeFormat.Relative(When);
        public string AbsoluteTime => When == default(DateTimeOffset)
            ? string.Empty
            : When.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");

        public bool HasUpstream => !string.IsNullOrEmpty(Upstream);
        public bool HasTrack => !string.IsNullOrEmpty(Track);

        /// <summary>
        ///     分支页分组显示用的标题，由 <see cref="GitPanelViewModel.SetBranches" /> 填入。
        ///     文案不在这里取：模型层不引界面翻译，否则 Git 命名空间就跟 UI 绑死了。
        /// </summary>
        public string GroupLabel { get; set; }
    }

    /// <summary>一个已配置的远程仓库及其 fetch URL。</summary>
    public sealed class GitRemoteInfo
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    /// <summary>
    ///     加载第二阶段产出的全部数据，由 <see cref="GitRepositoryReader.ReadDetails" /> 一次性返回。
    /// </summary>
    public sealed class GitRepositoryDetails
    {
        public List<GitCommitInfo> Commits { get; set; }
        public List<GitRefInfo> LocalBranches { get; set; }
        public List<GitRefInfo> RemoteBranches { get; set; }
        public List<GitRefInfo> Tags { get; set; }
        public List<GitRemoteInfo> Remotes { get; set; }
    }
}
