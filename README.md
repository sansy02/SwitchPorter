# SwitchPorter — Switch 相册管家

通过 USB 连接 Nintendo Switch，把截图与视频方便地保存到电脑或安卓手机。

## 功能

界面按「**从哪里看** → **送到哪里**」组织：

- **顶栏选来源**：「Switch 相册」（USB 连接的主机）/「电脑里的照片」（保存位置里已保存的）
- **底部操作栏选去处**：勾选照片后一键「存到电脑」「存到安卓手机」「发送到 iPhone」

功能细节：

- **游戏卡片墙**：类似 Switch 相册的界面，按游戏分组展示（封面 + 游戏名 + 数量），支持「全部」视图与**搜索游戏**；网格里可「选未保存的」、Ctrl+A 全选
- **存到电脑**：按**游戏分文件夹**保存到 `桌面\SwitchAlbum\游戏名\`；完成后状态栏可「打开文件夹」
- **直接存到安卓手机**：在「Switch 相册」里选好照片即可直传手机（`DCIM\SwitchAlbum\游戏名`），**不需要先存电脑**，电脑上只用临时文件、传完即删；也可以从「电脑里的照片」传
- **发送到 iPhone（零安装）**：显示二维码，iPhone 同一 WiFi 扫码后在 Safari 浏览——缩略图秒开、视频可直接播放、长按存入相册，或「下载 ZIP」后在文件 App 批量存入。从 Switch 直接发送时只下载到临时文件夹，退出软件自动清理。扫码打不开时，对话框里可切换电脑地址、一键放行防火墙
- **传输可靠**：每个文件传完都做完整性校验（大小 + 文件头），坏文件不会被记成「已保存」；传到手机后会回读手机目录确认文件真的存在
- **重复检测**：已保存的照片标记「已存电脑 / 已存手机 / 电脑+手机」，再次保存自动跳过；
  - 按 Switch 原始文件名识别，换系统语言、游戏改名不会重复保存
  - 电脑上的文件被删除或换了保存位置后可以重新保存
  - 按手机区分，换一部手机不会被误跳过
  - 设置里可「清除已保存记录」
- **自动重命名**：保存时按 `日期_游戏名_序号` 命名（可在设置中关闭）
- **进度**：实时显示传输速度与剩余时间，随时可取消
- **大图预览**：双击照片进入灯箱，方向键/滚轮切换，Esc 关闭；视频调用系统播放器
- **游戏封面**：内置约 3.5 万款游戏的中/英/日文名库（后台加载，启动不卡）；3000 款热门游戏封面离线内置，其余联网获取并缓存
- **Switch 2 兼容**：文件夹名智能反查；无标准名称的设备按 Album 目录兜底识别
- **过渡动画**：卡片错峰入场、视图切换、灯箱 crossfade；浅色/深色主题跟随系统

## 下载与使用

从 [Releases](https://github.com/sansy02/SwitchAlbum/releases) 下载最新版 `SwitchAlbum-vX.X.X-single.zip`（单文件 exe，含运行时），解压双击即可。

1. 用 USB 数据线连接 Switch 与电脑
2. 在 Switch 上进入 **系统设置 → 数据管理 → 管理截图与视频 → 通过 USB 连接复制到电脑**
3. 启动 SwitchAlbum，软件会自动扫描相册（也可点「重新扫描」）
4. 点开游戏勾选照片，在底部选择去处：「存到电脑」/「存到安卓手机」（手机用 USB 连接并选「传输文件」）/「发送到 iPhone」（扫码）

## 设置说明

- **保存位置**：默认 `桌面\SwitchAlbum`，可更改
- **自动重命名**：默认开启，`20250311_塞尔达传说 王国之泪_001.jpg`
- **手机目标设备**：默认自动检测；多部手机时可在设置中指定
- **打开保存文件夹**：资源管理器中打开保存目录
- **清除封面与缩略图缓存**：封面获取失败后的补救手段，清除后重新联网获取
- **清除「已保存」记录**：记录与实际不符时的补救（不会删除任何照片）
- **打开日志文件夹**：连接问题排查

数据目录 `%LocalAppData%\SwitchAlbum\`：`settings.json`（设置）、`covers\`（封面缓存）、`thumbs\`（缩略图缓存）、`saved.json`（已保存记录）、`logs\`（运行日志）。

## 封面数据

`Resources/titles.json`（约 10MB）由 [blawar/titledb](https://github.com/blawar/titledb) 的 CN.zh / HK.zh / US.en 生成，并合并 JP.ja 日文名，含名称与任天堂 eShop 图标/横幅直链；繁体名会额外注册简体变体（OpenCC 繁简表 + 品牌名别名，如 薩爾達→塞尔达、瑪利歐賽車→马力欧卡丁车）。重新生成：

```
dotnet run --project tools\TitleDbGenerator -- SwitchAlbum\Resources\titles.json <CN.zh.json> <HK.zh.json> <US.en.json> [JP.ja.json]
dotnet run --project tools\JaMerger -- SwitchAlbum\Resources\titles.json <JP.ja.json> <输出titles.json>   # 仅合并日文名（titledb 现版 CN.zh 已空，勿从零重新生成）
```

注意：titledb 现版 CN.zh 仅剩腾讯存根，直接用 TitleDbGenerator 重新生成会丢失中文名；日常更新请用 JaMerger 合并日文名。

## 开发

```
dotnet build SwitchAlbum.sln          # 构建
dotnet run --project SwitchAlbum.TestsHarness   # 纯函数与集成测试
dotnet run --project SwitchAlbum -- --smoke-test # 无界面端到端冒烟（结果写入 %TEMP%\SwitchAlbumSmokeTest.txt）
```

打包（单文件 exe）：

```
dotnet publish SwitchAlbum -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\single
```

图标由 `tools\IconGenerator` 生成（橙色像素风 N + 白色描边）。

## 已知限制

- 保存到安卓手机仅支持 MTP 模式（手机需选「传输文件」）；iPhone 通过 USB 无法写入相册（iOS 平台限制），本软件的苹果方案为零安装局域网网页（扫码 → 浏览器 → ZIP 批量存入相册）
- 相册文件夹名依赖 Switch 系统语言；中文/英文/日文文件夹名均已适配，未收录的游戏显示占位封面
- iPhone 方案要求手机与电脑在同一局域网；校园网 / 酒店 WiFi 常开启设备隔离，可改用手机热点
- 单文件 exe 可能被个别杀软误报，属无签名单文件打包的已知现象（右键属性 → 解除锁定后可正常运行）
