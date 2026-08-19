![git icon](./docs/git.svg)

# QuickLook.Plugin.GitViewer

[中文](./README.md) | **English**

Preview a git repository from Explorer by pressing <kbd>Space</kbd> on it — a plugin for
[QuickLook](https://github.com/QL-Win/QuickLook).

## What it shows

Select a repository folder, or its hidden `.git` folder, and press <kbd>Space</kbd>:

- **Overview** — current branch or detached `HEAD`, short commit hash, `git describe`,
  ahead/behind against the upstream, stash count, and a badge for bare / empty / detached state.
- **Commits** — the last 50 commits with hash, subject, ref decorations, author and relative time.
- **Branches** — local and remote-tracking branches, current branch marked, upstream tracking state.
- **Tags** — tag name, target object and message.
- **Remotes** — remote name and fetch URL.

Right-click a row to copy the commit hash, ref name or remote URL.
The panel follows the Windows light/dark theme and is localised in English and Simplified Chinese.

## Requirements

- QuickLook 4.x
- **Git for Windows** — the plugin shells out to `git.exe` rather than bundling a git library, so
  the preview always matches what your own git reports, including your config, `.gitignore`
  semantics, worktrees and submodules. `git.exe` is located via `PATH`, the `GitForWindows`
  registry key, and the usual install directories. If it is missing, the panel says so rather than
  failing silently.

All git commands are read-only and run with `--no-optional-locks`, so previewing a repository never
takes `index.lock` and never contends with an editor or terminal you have open on it.

## Install

1. Download the latest `.qlplugin` from the Releases page.
2. With QuickLook running, press <kbd>Space</kbd> on the downloaded `.qlplugin` file.
3. Click **Install**, then restart QuickLook.

## Development

1. Clone the repository, including submodules:
   `git clone --recursive https://github.com/kanami09/QL-GitViewer`
2. Build the `Release` configuration.
3. Run `Scripts\pack-zip.ps1` to produce `QuickLook.Plugin.GitViewer.qlplugin`.

To iterate without going through the installer, copy `bin\Release\` into the QuickLook user plugin
folder and restart QuickLook — plugins are only scanned once, at startup:

- installed build: `%AppData%\pooi.moe\QuickLook\QuickLook.Plugin\QuickLook.Plugin.GitViewer\`
- portable build: `<QuickLook folder>\UserData\QuickLook.Plugin\QuickLook.Plugin.GitViewer\`

`QuickLook.Common.dll` must **not** be shipped inside the plugin folder. QuickLook loads its own
copy, and a second one makes our `IViewer` a different type from the one QuickLook tests against,
so the plugin would load but never be used. The project reference is marked `Private=False` and the
packaging script excludes it.

Runtime errors are written to `QuickLook.Exception.log` in the QuickLook user data folder.

## License

MIT License.
