using QuickLook.Common.Helpers;
using System;
using System.IO;

namespace QuickLook.Plugin.GitViewer.Helpers
{
    /// <summary>
    ///     界面上的本地化文案，读取与本程序集放在一起的 Translations.config。
    /// </summary>
    internal static class Translate
    {
        private static readonly string TranslationFile = ResolveTranslationFile();

        /// <param name="id">翻译键，对应 Translations.config 里某个语言节点下的元素名。</param>
        /// <param name="failsafe">找不到文件或找不到该键时使用的英文兜底文案。</param>
        public static string Get(string id, string failsafe)
        {
            try
            {
                return TranslationHelper.Get(id, TranslationFile, failsafe: failsafe);
            }
            catch (Exception)
            {
                // 翻译文件缺失或损坏，绝不能因此让预览挂掉。
                return failsafe;
            }
        }

        /// <summary>
        ///     TranslationHelper 默认是相对 QuickLook.Common.dll（也就是 QuickLook 安装目录）去找文件的，
        ///     而通过 .qlplugin 安装的插件落在用户插件目录里，那条默认路径永远找不到我们。
        ///     显式传入路径，两种安装位置就都能工作。
        /// </summary>
        private static string ResolveTranslationFile()
        {
            try
            {
                var assembly = typeof(Translate).Assembly;

                var location = assembly.Location;
                if (string.IsNullOrEmpty(location) && !string.IsNullOrEmpty(assembly.CodeBase))
                    location = new Uri(assembly.CodeBase).LocalPath;

                var directory = Path.GetDirectoryName(location);
                if (string.IsNullOrEmpty(directory))
                    return null;

                return Path.Combine(directory, "Translations.config");
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
