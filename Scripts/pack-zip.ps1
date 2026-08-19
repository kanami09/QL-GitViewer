$ErrorActionPreference = 'Stop'

$root = Join-Path $PSScriptRoot '..'
$release = Join-Path $root 'bin\Release'

# 版本号取自 QuickLook.Plugin.Metadata.config，该文件由 update-version.ps1 在
# 构建前按 git describe 的结果写入，所以打包名和插件自报的版本号必然一致。
$metadata = [xml](Get-Content (Join-Path $root 'QuickLook.Plugin.Metadata.config'))
$version = $metadata.Metadata.Version
$name = "QuickLook.Plugin.GitViewer-$version"

$zip = Join-Path $root "$name.zip"
$plugin = Join-Path $root "$name.qlplugin"

if (-not (Test-Path $release)) {
    throw "请先构建 Release 配置：$release 不存在。"
}

# 连旧版本号的产物一起清掉，避免根目录里堆一堆分不清新旧的 .qlplugin。
Remove-Item (Join-Path $root 'QuickLook.Plugin.GitViewer*.qlplugin') -ErrorAction SilentlyContinue
Remove-Item $zip -ErrorAction SilentlyContinue

# QuickLook.Common.dll 由 QuickLook 主程序自带。插件目录里再放一份会被当成
# 另一个程序集加载，我们的 IViewer 就对不上 PluginManager 用来判断的那个，
# 插件会被静默忽略。csproj 里已经设了 Private=False，这里是双保险。
$files = Get-ChildItem -Path $release -Exclude *.pdb, *.xml, QuickLook.Common.dll

if (-not $files) {
    throw "$release 里没有可打包的文件。"
}

Compress-Archive -Path $files -DestinationPath $zip
Move-Item $zip $plugin

# 供 CI 引用：文件名相对于仓库根目录，工作流的工作目录正好是那里。
if ($env:GITHUB_OUTPUT) {
    "version=$version" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
    "plugin=$name.qlplugin" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
}

Write-Host "已生成 $plugin"
