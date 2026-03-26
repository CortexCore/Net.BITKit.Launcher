# Net.BITKit.Launcher

## 修订日志
- v1.0 - 2026-3-27
  - 初始版本发布

## 介绍
`Net.BITKit.Launcher`是一个基于Net Console的软件更新程序

通过获取远程服务器上的`manifest.yaml`获取远程文件的`md5`信息，进行对比

从而自动进行差异更新

## 目录预览
> Your_APP_Folder
>> Net.BITKit.Launcher.exe //更新程序
> 
>> manifest.yaml //文件清单
> 
>> .gitignore //忽略文件列表

## 使用方式:
```shell
./Net.BITKit.Launcher.exe #默认自动更新

./Net.BITKit.Launcher.exe generate #根据当前目录生成manifest.yaml文件

./Net.BITKit.Launcher.exe generate <dir> #根据指定目录生成manifest.yaml文件
```

生成`manifest.yaml`后，和程序文件一起放在文件服务上,并配置静态文件访问
程序将下载`https://domain.com/app_name/mainfest.yaml`作为文件清单

## 参数
### manifest.yaml
主要字段:
- `BaseUrl`: 文件服务器的基础URL，例如:`https://domain.com/app_name'
- `Launcher`: 启动路径，更新完成后，指定该路径下的可执行文件
### .gitignore
程序会忽略由`.gitignore`命中的文件和路径