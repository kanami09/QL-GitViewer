![git icon](./docs/git.svg)

# QuickLook.Plugin.GitViewer

**中文** | [English](./README_EN.md)

在资源管理器里选中一个 git 仓库，按 <kbd>空格</kbd> 就能预览它 —— 一个
[QuickLook](https://github.com/QL-Win/QuickLook) 插件。

## 能看到什么

选中仓库文件夹，或者 `.git` 文件夹，按 <kbd>空格</kbd>：

- **概览** —— 当前分支或分离的 `HEAD`、短提交哈希、`git describe` 结果、
  相对上游的领先/落后数量、贮藏（stash）数量，以及裸仓库 / 空仓库 / 分离 HEAD 状态的徽章。
- **提交** —— 从 HEAD 一直到首条提交，含哈希、提交说明、ref 装饰、作者和相对时间。
  点击一行可以展开，看到完整的提交正文，以及这次改动的文件列表。
- **分支** —— 本地分支与远程跟踪分支，当前分支有标记，并显示上游跟踪状态。
- **标签** —— 标签名、所指对象和说明。
- **远程** —— 远程名称与 fetch URL。

在任意一行上点右键，可以复制提交哈希、引用名或远程 URL。
面板会跟随 Windows 的浅色/深色主题，并提供简体中文与英文两套界面文案。

## 运行要求

- QuickLook 4.x
- **Git for Windows** —— 本插件通过调用 `git.exe` 读取数据，而不是内置一个 git 库，
  所以预览结果始终和使用 git 命令看到的一致，包括你的配置、`.gitignore` 语义、
  worktree 和 submodule 的处理。查找 `git.exe` 的顺序是 `PATH`、`GitForWindows`
  注册表项、常见安装目录。找不到时面板会直接说明，而不是静默失败。

所有 git 命令都是只读的，并且带 `--no-optional-locks`，因此预览仓库永远不会去写
`index.lock`，也不会和你开着的编辑器或终端抢锁。

## 安装

1. 从 Releases 页面下载最新的 `.qlplugin` 文件。
2. 保持 QuickLook 在后台运行，在下载好的 `.qlplugin` 文件上按 <kbd>空格</kbd>。
3. 点击弹窗里的 **Install**，然后重启 QuickLook。

## 开发

1. 克隆仓库，注意带上子模块：
   `git clone --recursive https://github.com/kanami09/QL-GitViewer`
2. 用 `Release` 配置构建。
3. 运行 `Scripts\pack-zip.ps1`，会在项目根目录生成 `QuickLook.Plugin.GitViewer-<版本号>.qlplugin`。

想跳过安装器快速迭代的话，把 `bin\Release\` 的内容拷进 QuickLook 的用户插件目录再重启
QuickLook —— 插件只在启动时扫描一次，不重启不会生效：

- 安装版：`%AppData%\pooi.moe\QuickLook\QuickLook.Plugin\QuickLook.Plugin.GitViewer\`
- 便携版：`<QuickLook 目录>\UserData\QuickLook.Plugin\QuickLook.Plugin.GitViewer\`

`QuickLook.Common.dll` **不能**放进插件目录。QuickLook 自己会加载一份，插件目录里再放一份的话，
我们实现的 `IViewer` 和 QuickLook 用来做类型判断的 `IViewer` 就不是同一个类型了 ——
结果是插件能加载但永远不被识别，而且没有任何报错。项目引用已标记为 `Private=False`，
打包脚本里也做了排除。

运行期的异常会写进 QuickLook 用户数据目录下的 `QuickLook.Exception.log`。

## 许可证

MIT License.
