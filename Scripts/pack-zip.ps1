$ErrorActionPreference = 'Stop'

$root = Join-Path $PSScriptRoot '..'
$release = Join-Path $root 'bin\Release'
$zip = Join-Path $root 'QuickLook.Plugin.GitViewer.zip'
$plugin = Join-Path $root 'QuickLook.Plugin.GitViewer.qlplugin'

if (-not (Test-Path $release)) {
    throw "请先构建 Release 配置：$release 不存在。"
}

Remove-Item $plugin -ErrorAction SilentlyContinue
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

Write-Host "已生成 $plugin"
