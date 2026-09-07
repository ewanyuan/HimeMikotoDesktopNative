# Hime & Mikoto Desktop Pet

Windows 桌面宠物：让 Hime 或 Mikoto 以透明、无边框的方式显示在桌面上。

A Windows desktop pet that displays Hime or Mikoto in a transparent, borderless desktop layer.

## 这是什么 | What it is

这是一个桌面宠物宿主。它用 Babylon.js + babylon-mmd 直接读取 PMX 模型和 VMD 动作，不使用 Godot，也不把模型转换成 OBJ/GLB。

This is the desktop-pet host. It loads PMX models and VMD motions with Babylon.js + babylon-mmd. It does not use Godot or convert the models to OBJ/GLB.

## 有什么 | Features

- Hime / Mikoto 二选一显示，可在右键菜单切换。
- 左键按住人物即可拖动；右键打开菜单。
- 透明无边框，默认始终置顶。
- 支持 VMD 舞蹈、循环播放、双人舞、肤色、眼睛表情和嘴部表情。
- 不需要大模型，固定功能可离线运行；当前不包含聊天功能。

- Show Hime or Mikoto, and switch between them from the right-click menu.
- Drag the pet with the left mouse button; open the menu with the right button.
- Transparent and borderless; always-on-top by default.
- Supports VMD dances, looping, dual-character dances, skin tones, eye expressions, and mouth expressions.
- No LLM is required for the built-in features. Chat is not included yet.

## 先说下载 | Before downloading

当前 GitHub 仓库是源码仓库，不是即开即用的发行包。仓库目前没有上传可直接运行的 exe，也没有上传 PMX 模型、VMD 动作或音乐。

This GitHub repository currently contains source code, not a ready-to-run release. It does not include a downloadable executable, PMX models, VMD motions, or music.

因此，别人只下载 GitHub ZIP，不能直接看到人物。即使只拿一个 exe 文件，也不够：程序还需要完整的运行时文件、模型资源和 WebView2。

Downloading the GitHub ZIP alone will not show the pet. An exe file by itself is also not enough; the app needs its runtime files, model assets, and WebView2.

## 从源码运行 | Run from source

### 需要 | Requirements

- Windows 10/11 x64
- .NET 10 SDK
- Node.js 和 pnpm
- Microsoft Edge WebView2 Runtime
- 你有权使用的 Hime / Mikoto PMX 模型及其贴图

- Windows 10/11 x64
- .NET 10 SDK
- Node.js and pnpm
- Microsoft Edge WebView2 Runtime
- Hime / Mikoto PMX models and textures that you are allowed to use

### 准备目录 | Prepare the folders

把模型放在本仓库同级的 `HimeMikotoDesktop/assets/Hime_&_Mikoto/`，保留下面的文件名和目录结构：

Place the models in the sibling folder `HimeMikotoDesktop/assets/Hime_&_Mikoto/`, keeping this layout:

```text
<parent-folder>/
├─ HimeMikotoDesktop/
│  └─ assets/Hime_&_Mikoto/
│     ├─ Hime_260426/Hime.physics-stable.pmx
│     └─ Mikoto_260303/Mikoto.physics-stable.pmx
└─ HimeMikotoDesktopNative/
```

动作放入本仓库的 `assets/motions/`，音乐放入 `assets/music/`。动作和音乐都不是运行必需项；请只使用自己有权使用的素材。

Put motions in `assets/motions/` and music in `assets/music/`. Motions and music are optional. Only use assets that you are authorized to use.

### 启动 | Start

在仓库目录执行：

From the repository directory, run:

```powershell
cd mmd-runtime
pnpm install
cd ..
powershell -ExecutionPolicy Bypass -File .\run-native.ps1
```

也可以双击 `run-native.vbs` 启动。

You can also double-click `run-native.vbs`.

## 操作 | Controls

- 左键按住人物：拖动桌宠
- 右键人物：打开菜单
- `Esc` 或 `Alt+F4`：退出

- Hold the left mouse button: drag the pet
- Right-click the pet: open the menu
- `Esc` or `Alt+F4`: exit

## 资源与许可 | Assets and licensing

代码和第三方模型、动作、音乐不是同一套许可。模型、动作和音乐不随本仓库再分发；使用前请阅读各自的原始说明和条款。动作来源记录见 [`assets/motions/MOTION-CREDITS.md`](assets/motions/MOTION-CREDITS.md)。本仓库当前未附带开源许可证。

The code and third-party models, motions, and music do not share the same license. Third-party assets are not redistributed here; read their original terms before use. Motion credits are listed in [`assets/motions/MOTION-CREDITS.md`](assets/motions/MOTION-CREDITS.md). This repository currently has no open-source license.
