@ECHO OFF

:: 编译 EasyInstall.Server
set EasyInstallServerProjectPath=..\EasyInstall.Server\EasyInstall.Server.sln
set EasyInstallServerReleasePath=..\EasyInstall.Server\EasyInstall.Server\bin\Release\net8.0\publish

:: 删除旧发布目录
rd /s /q "%EasyInstallServerReleasePath%" 2>nul

:: 删除旧的 obj，避免旧的 project.assets.json 干扰
rd /s /q "..\EasyInstall.Server\EasyInstall.Server\obj" 2>nul

:: 获取时间
set DateTime=%date:~0,4%%date:~5,2%%date:~8,2%%time:~0,2%%time:~3,2%
set DateTime=%DateTime: =%

:: 还原 NuGet，并指定 Windows x64
dotnet restore "%EasyInstallServerProjectPath%" -r win-x64

:: 编译并发布
MSBuild "%EasyInstallServerProjectPath%" ^
    /t:Build ^
    /p:Configuration=Release ^
    /p:DeployOnBuild=True ^
    /p:PublishProfile=FolderProfile

:: 压缩发布文件
powershell -Command "Compress-Archive -Path '%EasyInstallServerReleasePath%\*' -DestinationPath 'EasyInstallServerSetup_%DateTime%.zip' -Force"

:: 编译 EasyInstall
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
