using System;
using System.IO;

namespace QuickLook.Plugin.GitViewer.Git
{
    /// <summary>
    ///     判断 shell 选中的路径是不是一个 git 仓库，只做文件系统检查。
    ///     每次按空格都会经由 <c>IViewer.CanHandle</c> 调到这里，而且 PluginManager.FindMatch
    ///     还会用 Stopwatch 给它计时，所以必须足够廉价。
    /// </summary>
    public static class GitRepositoryLocator
    {
        /// <summary>
        ///     返回针对 <paramref name="path" /> 应该在哪个目录启动 git.exe；不是仓库则返回 null。
        ///     绝不抛异常：FindMatch 会把异常静默吞掉，那样崩溃就变成了"悄无声息地不匹配"，更难排查。
        /// </summary>
        public static RepositoryLocation Locate(string path)
        {
            try
            {
                return LocateCore(path);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static RepositoryLocation LocateCore(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            // shell 传来的路径可能带、也可能不带尾部分隔符，而
            // Path.GetFileName(@"C:\repo\.git\") 会返回空串。
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(trimmed))
                return null;

            if (!Directory.Exists(trimmed))
                return null;

            var name = Path.GetFileName(trimmed);

            // 情况一：路径就是 ".git" 目录本身。必须同时校验 HEAD + objects + refs，
            // 否则任何恰好叫 ".git" 的普通文件夹都会被误判成仓库。
            if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase) && IsGitDir(trimmed))
            {
                var workTree = Path.GetDirectoryName(trimmed);
                if (string.IsNullOrEmpty(workTree))
                    return null;

                return new RepositoryLocation
                {
                    StartDirectory = workTree,
                    DisplayName = FolderName(workTree),
                    SelectedGitDir = true
                };
            }

            // 情况二：路径是工作区根目录。普通克隆里 ".git" 是目录，submodule 或
            // 链接工作树里它是一个写着 "gitdir: ..." 的文件；两者都算仓库。
            var dotGit = Path.Combine(trimmed, ".git");
            if (Directory.Exists(dotGit) || File.Exists(dotGit))
                return new RepositoryLocation
                {
                    StartDirectory = trimmed,
                    DisplayName = FolderName(trimmed),
                    SelectedGitDir = false
                };

            return null;
        }

        private static bool IsGitDir(string candidate)
        {
            return File.Exists(Path.Combine(candidate, "HEAD"))
                   && Directory.Exists(Path.Combine(candidate, "objects"))
                   && Directory.Exists(Path.Combine(candidate, "refs"));
        }

        /// <summary>取文件夹名；遇到 "D:\" 这类盘符根目录时回退成完整路径。</summary>
        private static string FolderName(string directory)
        {
            var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? directory : name;
        }
    }
}
