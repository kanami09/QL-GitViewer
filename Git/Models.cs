using System;

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

    /// <summary>提交历史里的一条记录。</summary>
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

        public string RelativeTime => TimeFormat.Relative(When);
        public string AbsoluteTime => When.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        public string AuthorTooltip => string.IsNullOrEmpty(AuthorEmail) ? AuthorName : AuthorName + " <" + AuthorEmail + ">";
        public bool HasDecorations => !string.IsNullOrEmpty(Decorations);

        /// <summary>把装饰拆成一个个小标签，例如 "HEAD -&gt; master"、"tag: v1"。</summary>
        public string[] DecorationList => string.IsNullOrEmpty(Decorations)
            ? new string[0]
            : Decorations.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
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
        ///     分支页分组显示用的标题。本地与远程合并在同一个列表里、
        ///     靠分组分隔，而不是用两个独立列表 —— 后者的选中状态各管各的，
        ///     会同时出现两行高亮。
        /// </summary>
        public string GroupLabel => Kind == GitRefKind.RemoteBranch
            ? Translate.Get("SectionRemoteBranches", "Remote-tracking")
            : Translate.Get("SectionLocalBranches", "Local");
    }

    /// <summary>一个已配置的远程仓库及其 fetch URL。</summary>
    public sealed class GitRemoteInfo
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }
}
