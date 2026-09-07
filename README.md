# Hime & Mikoto Desktop Pet

Windows 桌面宠物：让 Hime 或 Mikoto 以透明、无边框的方式显示在桌面上。

A Windows desktop pet that displays Hime or Mikoto in a transparent, borderless desktop layer.

## 下载与启动 | Download and start

### 当前发行包 | Current release

现在已经有可直接运行的 [v0.1.0 便携版压缩包](https://github.com/ewanyuan/HimeMikotoDesktopNative/releases/download/v0.1.0/portable.zip)。这里说的“发行包”，是作者把 exe、运行库、`web` 和 `assets` 一起打好的完整压缩包，不是 GitHub 的源码 ZIP。

The ready-to-run [v0.1.0 portable package](https://github.com/ewanyuan/HimeMikotoDesktopNative/releases/download/v0.1.0/portable.zip) is now available. A “release package” means one complete archive containing the exe, runtime files, `web`, and `assets`—not GitHub's source ZIP.

下载后：

1. 解压整个压缩包；
2. 双击 `HimeMikotoDesktopNative.exe`。

After downloading, extract the whole archive and double-click `HimeMikotoDesktopNative.exe`.

不要下载 **Code → Download ZIP** 来启动桌宠；那是源码。也不要只复制 exe 文件，便携包里的文件必须保留在一起。

Do not use **Code → Download ZIP** to start the pet; that is source code. Do not copy only the exe—the portable package must remain together.

如果启动时提示缺少 WebView2，请安装 Microsoft Edge WebView2 Runtime 后重新启动。Windows 10/11 x64 是当前支持目标。

If WebView2 is missing, install the Microsoft Edge WebView2 Runtime and start the app again. The current target is Windows 10/11 x64.

## 功能 | Features

- 右键切换 Hime / Mikoto、肤色和表情；
- 左键按住人物拖动，右键打开菜单；
- 透明无边框、默认置顶；
- 支持 VMD 舞蹈、循环播放和双人舞；
- 内置功能不需要大模型，当前没有聊天功能。

- Switch characters, skin tones, and expressions from the right-click menu.
- Drag the pet with the left mouse button; open the menu with the right button.
- Transparent, borderless, and always-on-top by default.
- Supports VMD dances, looping, and dual-character dances.
- No LLM is needed for built-in features; chat is not included.

## 开发者创建便携包 | Build a portable package

先确保本机已经有 `.NET 10 SDK`、Node.js、pnpm 和 WebView2 Runtime。然后在本目录执行：

```powershell
pnpm --dir .\mmd-runtime install
powershell -ExecutionPolicy Bypass -File .\tools\package-portable.ps1 -IncludeLocalPrivateAssets
```

完成后，发行包和压缩包位于 `dist/portable` 与 `dist/portable.zip`。`-IncludeLocalPrivateAssets` 会把本机模型、动作和音乐复制进便携包，适合个人使用；不要未经许可把第三方素材上传或再分发。

The `-IncludeLocalPrivateAssets` switch creates a personal package with local models, motions, and music. Do not upload or redistribute third-party assets without permission.

不带这个选项也可以生成只有程序的包，但需要自行把有权使用的模型放入：

```text
assets/Hime_&_Mikoto/Hime_260426/Hime.physics-stable.pmx
assets/Hime_&_Mikoto/Mikoto_260303/Mikoto.physics-stable.pmx
```

## 操作 | Controls

- 左键按住人物：拖动桌宠；
- 右键人物：打开菜单；
- `Esc` 或 `Alt+F4`：退出。

- Hold the left mouse button: drag the pet.
- Right-click the pet: open the menu.
- `Esc` or `Alt+F4`: exit.

## 资源与许可 | Assets and licensing

代码和第三方模型、动作、音乐不是同一套许可。模型、动作和音乐不随公开源码仓库再分发；使用前请阅读各自原始说明和条款。动作来源记录见 [`assets/motions/MOTION-CREDITS.md`](assets/motions/MOTION-CREDITS.md)。本仓库当前未附带开源许可证。

Code and third-party models, motions, and music do not share one license. Third-party assets are not redistributed in the public source repository; read their original terms before use. Motion credits are listed in [`assets/motions/MOTION-CREDITS.md`](assets/motions/MOTION-CREDITS.md). This repository currently has no open-source license.
