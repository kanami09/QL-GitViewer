Remove-Item ..\QuickLook.Plugin.GitViewer.qlplugin -ErrorAction SilentlyContinue

$files = Get-ChildItem -Path ..\bin\Release\ -Exclude *.pdb,*.xml
Compress-Archive $files ..\QuickLook.Plugin.GitViewer.zip
Move-Item ..\QuickLook.Plugin.GitViewer.zip ..\QuickLook.Plugin.GitViewer.qlplugin