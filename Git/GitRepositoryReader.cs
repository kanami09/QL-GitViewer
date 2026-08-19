using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace QuickLook.Plugin.GitViewer.Git
{
    /// <summary>
    ///     把 git.exe 的输出转换成面板绑定用的模型对象。
    ///     加载分成两个阶段，这样头部概览可以先画出来，不必等较慢的明细查询。
    /// </summary>
    public sealed class GitRepositoryReader
    {
        /// <summary>单元分隔符，用来分隔一条记录内部的各个字段。</summary>
        private const char FieldSeparator = '\x1f';

        /// <summary>记录分隔符，这样提交说明里可以出现任何字符，包括换行。</summary>
        private const char RecordSeparator = '\x1e';

        private const int CommitLimit = 50;

        private readonly GitCommandRunner _runner;

        public GitRepositoryReader(GitCommandRunner runner)
        {
            _runner = runner;
        }

        /// <summary>第一阶段：产出头部概览的那几条廉价查询。</summary>
        public GitRepositoryInfo ReadOverview(CancellationToken ct)
        {
            var info = new GitRepositoryInfo();

            var layout = _runner.Run(ct, "rev-parse", "--absolute-git-dir", "--is-bare-repository");
            if (layout.Success)
            {
                var lines = SplitLines(layout.StdOut);
                if (lines.Count > 0) info.GitDir = lines[0];
                if (lines.Count > 1) info.IsBare = string.Equals(lines[1], "true", StringComparison.OrdinalIgnoreCase);
            }

            // --show-toplevel 在裸仓库上会失败，所以单独发一次。
            if (!info.IsBare)
            {
                var topLevel = _runner.Run(ct, "rev-parse", "--show-toplevel");
                if (topLevel.Success)
                    info.WorkTree = topLevel.Line;
            }

            // 这里退出码非 0 说明 HEAD 指向的是某个提交而不是分支，即分离 HEAD。
            var symbolic = _runner.Run(ct, "symbolic-ref", "--short", "-q", "HEAD");
            if (symbolic.Success && symbolic.Line.Length > 0)
                info.BranchName = symbolic.Line;
            else
                info.IsDetached = true;

            // 这里退出码非 0 说明 HEAD 尚未诞生，即仓库还没有任何提交。
            var head = _runner.Run(ct, "rev-parse", "--short", "HEAD");
            if (head.Success && head.Line.Length > 0)
                info.ShortHash = head.Line;
            else
                info.IsEmpty = true;

            // 全新仓库只是"还没有提交"，说它分离 HEAD 没有意义，所以只标记为空。
            if (info.IsEmpty)
                info.IsDetached = false;

            if (!info.IsEmpty)
            {
                var describe = _runner.Run(ct, "describe", "--tags", "--always");
                if (describe.Success)
                    info.Describe = describe.Line;

                var upstream = _runner.Run(ct, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}");
                if (upstream.Success && upstream.Line.Length > 0)
                {
                    info.HasUpstream = true;
                    info.Upstream = upstream.Line;

                    var counts = _runner.Run(ct, "rev-list", "--left-right", "--count", "HEAD...@{u}");
                    if (counts.Success)
                    {
                        var parts = counts.Line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            info.Ahead = ParseInt(parts[0]);
                            info.Behind = ParseInt(parts[1]);
                        }
                    }
                }
            }

            // refs/stash 不存在时退出码非 0，那就是单纯没有 stash。
            var stash = _runner.Run(ct, "rev-list", "--walk-reflogs", "--count", "refs/stash");
            if (stash.Success)
                info.StashCount = ParseInt(stash.Line);

            return info;
        }

        /// <summary>第二阶段：明细查询，并行执行。</summary>
        public GitRepositoryDetails ReadDetails(CancellationToken ct)
        {
            var commitsTask = Task.Run(() => ReadCommits(ct), ct);
            var refsTask = Task.Run(() => ReadRefs(ct), ct);
            var remotesTask = Task.Run(() => ReadRemotes(ct), ct);

            Task.WaitAll(new Task[] { commitsTask, refsTask, remotesTask });

            var details = new GitRepositoryDetails
            {
                Commits = commitsTask.Result,
                Remotes = remotesTask.Result,
                LocalBranches = new List<GitRefInfo>(),
                RemoteBranches = new List<GitRefInfo>(),
                Tags = new List<GitRefInfo>()
            };

            foreach (var item in refsTask.Result)
                switch (item.Kind)
                {
                    case GitRefKind.LocalBranch:
                        details.LocalBranches.Add(item);
                        break;
                    case GitRefKind.RemoteBranch:
                        details.RemoteBranches.Add(item);
                        break;
                    case GitRefKind.Tag:
                        details.Tags.Add(item);
                        break;
                }

            return details;
        }

        private List<GitCommitInfo> ReadCommits(CancellationToken ct)
        {
            var commits = new List<GitCommitInfo>();

            // %b 放在最后：它可以包含任意字符（含换行），而记录之间靠 %x1e 分隔，
            // 所以多行正文不会破坏解析。
            const string format = "--pretty=format:%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%D%x1f%s%x1f%P%x1f%b%x1e";
            var result = _runner.Run(ct, "log", "-n", CommitLimit.ToString(CultureInfo.InvariantCulture),
                "--no-color", format);

            if (!result.Success)
                return commits;

            foreach (var record in result.StdOut.Split(RecordSeparator))
            {
                var trimmed = record.TrimStart('\r', '\n');
                if (trimmed.Length == 0)
                    continue;

                var f = trimmed.Split(FieldSeparator);
                if (f.Length < 9)
                    continue;

                commits.Add(new GitCommitInfo
                {
                    Hash = f[0],
                    ShortHash = f[1],
                    AuthorName = f[2],
                    AuthorEmail = f[3],
                    When = ParseDate(f[4]),
                    Decorations = f[5],
                    Subject = f[6],
                    ParentCount = CountParents(f[7]),
                    // %b 末尾总带一个换行，不去掉的话展开后正文下面会多出一片空白。
                    Body = f[8].TrimEnd('\r', '\n')
                });
            }

            return commits;
        }

        /// <summary>
        ///     读取单个提交的文件改动，供展开某一行时惰性调用。
        ///     <para>
        ///     实测：<c>--numstat</c> 与 <c>--name-status</c> 同时给的话后者会覆盖前者，
        ///     两样拿不齐。<c>--raw</c> 与 <c>--numstat</c> 同时给才行 —— git 会先后输出
        ///     两个块，文件顺序一致。状态和路径取自 raw 块（重命名的新旧路径是两个独立
        ///     字段，干净），增删行数取自 numstat 块（那里的重命名路径被压缩成
        ///     "{a =&gt; b}" 形式，不适合用来取路径）。
        ///     </para>
        /// </summary>
        /// <returns>
        ///     文件改动列表；命令失败时返回 null，好和"这个提交确实没有改动"区分开。
        /// </returns>
        public List<GitFileChange> ReadCommitFiles(string hash, CancellationToken ct)
        {
            var result = _runner.Run(ct, "show", "--no-color", "--format=", "--raw", "--numstat",
                "--find-renames", hash);

            if (!result.Success)
                return null;

            var changes = new List<GitFileChange>();

            // null 表示"这一项是二进制文件，没有行数"。二进制项也必须占一个位置，
            // 否则它后面所有文件的行数都会串到上一行去。
            var counts = new List<int[]>();

            foreach (var line in SplitLines(result.StdOut))
            {
                if (line[0] == ':')
                {
                    var change = ParseRawLine(line);
                    if (change != null)
                        changes.Add(change);

                    continue;
                }

                var tabs = line.Split('\t');
                if (tabs.Length < 3)
                    continue;

                counts.Add(tabs[0] == "-" || tabs[1] == "-"
                    ? null
                    : new[] { ParseInt(tabs[0]), ParseInt(tabs[1]) });
            }

            // 两个块理应一一对应。数目对不上就只保留状态和路径，行数留 0 ——
            // 宁可少显示一点，也不能让一次解析意外把整个展开搞崩。
            if (counts.Count == changes.Count)
                for (var i = 0; i < changes.Count; i++)
                {
                    if (counts[i] == null)
                    {
                        changes[i].IsBinary = true;
                        continue;
                    }

                    changes[i].Added = counts[i][0];
                    changes[i].Deleted = counts[i][1];
                }

            return changes;
        }

        /// <summary>
        ///     解析一行 raw 输出：<c>:100644 100644 aaa bbb R095\t旧路径\t新路径</c>。
        ///     前面那段用空格分隔，最后一个空格分隔字段是状态，其后全部按制表符分隔。
        /// </summary>
        private static GitFileChange ParseRawLine(string line)
        {
            var tabs = line.Split('\t');
            if (tabs.Length < 2)
                return null;

            var head = tabs[0].Split(' ');
            var status = head[head.Length - 1];
            if (status.Length == 0)
                return null;

            var change = new GitFileChange
            {
                Status = status,
                Kind = ToChangeKind(status[0])
            };

            // 重命名和复制带两个路径，其余只有一个。
            if ((change.Kind == GitFileChangeKind.Renamed || change.Kind == GitFileChangeKind.Copied)
                && tabs.Length >= 3)
            {
                change.OldPath = tabs[1];
                change.Path = tabs[2];
            }
            else
            {
                change.Path = tabs[1];
            }

            return change;
        }

        private static GitFileChangeKind ToChangeKind(char status)
        {
            switch (status)
            {
                case 'A': return GitFileChangeKind.Added;
                case 'M': return GitFileChangeKind.Modified;
                case 'D': return GitFileChangeKind.Deleted;
                case 'R': return GitFileChangeKind.Renamed;
                case 'C': return GitFileChangeKind.Copied;
                case 'T': return GitFileChangeKind.TypeChanged;
                default: return GitFileChangeKind.Unknown;
            }
        }

        /// <summary>%P 是空格分隔的父提交哈希；数出来大于 1 即合并提交。</summary>
        private static int CountParents(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            return value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private List<GitRefInfo> ReadRefs(CancellationToken ct)
        {
            var refs = new List<GitRefInfo>();

            // 一次调用就把本地分支、远程跟踪分支和标签全拿到。
            // %(upstream:track) 直接给出 "[ahead 3, behind 1]"，省掉逐分支跑 rev-list。
            const string format = "--format=%(refname)%1f%(objectname:short)%1f%(committerdate:iso-strict)" +
                                  "%1f%(upstream:short)%1f%(upstream:track)%1f%(HEAD)%1f%(authorname)" +
                                  "%1f%(contents:subject)";

            var result = _runner.Run(ct, "for-each-ref", "--sort=-committerdate", format,
                "refs/heads", "refs/remotes", "refs/tags");

            if (!result.Success)
                return refs;

            foreach (var line in SplitLines(result.StdOut))
            {
                var f = line.Split(FieldSeparator);
                if (f.Length < 8)
                    continue;

                var fullName = f[0];
                GitRefKind kind;
                string shortName;

                if (StripPrefix(fullName, "refs/heads/", out shortName))
                    kind = GitRefKind.LocalBranch;
                else if (StripPrefix(fullName, "refs/remotes/", out shortName))
                    kind = GitRefKind.RemoteBranch;
                else if (StripPrefix(fullName, "refs/tags/", out shortName))
                    kind = GitRefKind.Tag;
                else
                    continue;

                // "origin/HEAD" 只是指向远程默认分支的符号引用，本身不是一个分支。
                if (kind == GitRefKind.RemoteBranch && shortName.EndsWith("/HEAD", StringComparison.Ordinal))
                    continue;

                refs.Add(new GitRefInfo
                {
                    Kind = kind,
                    Name = shortName,
                    ObjectName = f[1],
                    When = ParseDate(f[2]),
                    Upstream = f[3],
                    Track = f[4],
                    IsHead = f[5] == "*",
                    AuthorName = f[6],
                    Subject = f[7]
                });
            }

            return refs;
        }

        private List<GitRemoteInfo> ReadRemotes(CancellationToken ct)
        {
            var remotes = new List<GitRemoteInfo>();

            // 比 "git remote -v" 更便宜，而且每个远程只出现一次，不用去重 fetch/push 两行。
            var result = _runner.Run(ct, "config", "--get-regexp", "^remote[.].*[.]url$");
            if (!result.Success)
                return remotes;

            foreach (var line in SplitLines(result.StdOut))
            {
                var space = line.IndexOf(' ');
                if (space <= 0)
                    continue;

                var key = line.Substring(0, space);
                var url = line.Substring(space + 1);

                // 键形如 "remote.origin.url"，而远程名本身也可能带点，所以只能掐头去尾。
                if (!key.StartsWith("remote.", StringComparison.Ordinal) ||
                    !key.EndsWith(".url", StringComparison.Ordinal))
                    continue;

                var name = key.Substring("remote.".Length, key.Length - "remote.".Length - ".url".Length);
                if (name.Length == 0)
                    continue;

                remotes.Add(new GitRemoteInfo { Name = name, Url = url });
            }

            return remotes;
        }

        private static bool StripPrefix(string value, string prefix, out string remainder)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                remainder = value.Substring(prefix.Length);
                return true;
            }

            remainder = null;
            return false;
        }

        private static List<string> SplitLines(string value)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(value))
                return lines;

            foreach (var line in value.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.Length > 0)
                    lines.Add(trimmed);
            }

            return lines;
        }

        private static int ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static DateTimeOffset ParseDate(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default(DateTimeOffset);

            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
                ? parsed
                : default(DateTimeOffset);
        }
    }
}
