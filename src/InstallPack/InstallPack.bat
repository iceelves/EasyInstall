@ECHO OFF

:: 应用变量
set ProjectPath=..\EasyInstall\EasyInstall.sln
set ReleasePath=..\EasyInstall\EasyInstall\bin\Release

:: 删除生成文件夹
rd/s/q %ReleasePath%

:: nuget 引用
nuget restore %ProjectPath%

:: 编译代码
MSBuild %ProjectPath% /property:Configuration=Release

:: 打包
EasyInstall.exe EasyInstall.json

pause
exit
