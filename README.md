# McPack —— Minecraft 单文件打包器

> 一个可视化的 Minecraft 打包工具，将任意版本（Vanilla / Fabric / Forge / NeoForge）
> 的 Minecraft + JDK 打包成单个 exe 文件。双击即玩，不解压、零驱动、零管理员、零外部依赖。

打包器本身也是单文件 NativeAOT 应用，生成的 `McPack.exe` = **~3.4 MiB NativeAOT 宿主** + **游戏数据尾部 Store zip 容器**。

核心是纯用户态 `ntdll` hook 虚拟文件系统（`Z:\` 虚拟根、假句柄表、假 `SEC_IMAGE` 内存加载 `jvm.dll`）+ JNI 进程内 JVM + 容器 mmap 手动解析 zip。

---

## 目录

- [项目简介](#项目简介)
- [快速开始](#快速开始)
- [打包器使用](#打包器使用)
- [技术架构](#技术架构)
- [构建](#构建)
- [目录结构](#目录结构)
- [已知限制](#已知限制)
- [许可证](#许可证)

---

## 项目简介

McPack 包含两个部分：

### 打包器（`McPack.Packager`）

一个带 GUI 的打包工具，用于将 Minecraft 和 JDK 打包成单文件 exe：

- **可视化界面**：选择游戏目录（`.minecraft`）和 JDK 目录，一键打包。
- **任意版本**：自动检测版本 ID 和入口类，支持 Vanilla、Fabric、Forge、NeoForge。
- **同步清单**：可在 GUI 中勾选 `mods`、`resourcepacks`、`shaderpacks`、`config` 等目录，打包后首次运行自动从容器同步到可写 gameDir。
- **单文件发布**：打包器本身也是 NativeAOT 编译的单文件 exe，无任何依赖。

### 启动器（`McPack`）

打包后的 exe 内置的启动引擎：

- **单文件交付**：只分发一个 exe 文件，内部包含完整游戏数据。
- **零解压**：容器内条目全部 Store（不压缩），运行时 mmap 按偏移直读，不落地。
- **零驱动 / 零管理员**：纯用户态 ntdll hook，无内核组件、无需 UAC。
- **零外部依赖**：NativeAOT + `mcpack_hooks_static.lib` 静态链接，exe 之外无任何 dll。
- **离线单机**：无微软账号登录；用户名取自 exe 文件名（重命名 exe 即可改名）。
- **存档持久**：gameDir 是真实可写目录，存档 / 配置 / mods 持久保存。

环境基线：Windows 11 26100，.NET 10，x64。

---

## 快速开始

### 运行已打包的 exe

1. 双击 `McPack.exe`。
2. 进程启动 JVM，拉起 Minecraft，等待主菜单窗口出现。
3. 关闭游戏窗口后进程退出。

启动后生成：

- `game\`：gameDir（真实、可写、持久）。`saves\` 存档、`mods\`、`config\`、`resourcepacks\` 都在这。
- `logs\`：游戏日志。
- natives 虚拟化：JVM natives 全部位于虚拟 `Z:\cache\natives\`（内存，不落盘）。

### 调试环境变量

| 变量 | 取值 | 作用 |
|---|---|---|
| `MCPACK_VERBOSE_HOOKS` | `1` / `true` | 开启 ntdll hook 全量日志 |
| `MCPACK_NO_GPU_FORCE` | `1` / `true` | 关闭强制独显（不写 GPU 偏好、不自动重启） |

### 强制独显（双显卡笔记本）

游戏进程名是 `SingleFileMc.exe`，Windows/NVIDIA 不认识该进程时默认把它分给核显。
启动器启动时会把自己写入 `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`
（`GpuPreference=2`，即 Windows 图形设置里的"高性能"），检测到混合显卡（核显+独显）后
自动重启自身一次让偏好生效，之后每次启动都直接走独显，无需手动配置 NVIDIA 控制面板。
可用环境变量 `MCPACK_NO_GPU_FORCE=1` 完全关闭。

---

## 打包器使用

### 构建打包器

```powershell
# 发布打包器为单文件 exe
dotnet publish McPack.Packager -c Release -r win-x64
```

产物：`McPack.Packager/bin/Release/net10.0-windows/win-x64/publish/McPack.Packager.exe`

### 使用打包器

1. 先构建启动器 stub（打包器需要它作为 exe 前半部分）：
   ```powershell
   dotnet publish McPack -c Release -r win-x64
   ```

2. 运行 `McPack.Packager.exe`，在 GUI 中：
   - **游戏目录**：选择 `.minecraft` 文件夹（含 `versions/`、`libraries/`、`assets/`）
   - **JDK 目录**：选择 JDK 根目录（含 `bin/server/jvm.dll`）
   - **启动器 Stub**：自动检测，也可手动选择第 1 步构建的 `McPack.exe`
   - **输出文件**：生成的 `McPack.exe` 保存位置
   - **同步到 game/**：勾选需要在运行时自动同步的目录（mods、resourcepacks 等）

3. 点击 **打包**，等待完成。进度条和日志实时显示进度。

### 同步清单

打包时勾选的目录会被写入容器内的 `.mcpack-sync` 清单文件。首次运行 `McPack.exe` 时，启动器会按清单将文件从只读容器复制到可写 `game/` 目录，已存在的文件不会重复覆盖。

### 版本兼容性

打包器支持任意 Minecraft 版本，自动从 `version.json` 中读取：

| 版本类型 | 示例入口类 |
|---------|-----------|
| Vanilla | `net.minecraft.client.main.Main` |
| Forge | `net.minecraftforge.fml.loading.ModLauncher` |
| Fabric | `net.fabricmc.loader.impl.launch.knot.KnotClient` |
| NeoForge | `net.neoforged.fml.startup.Client` |

---

## 技术架构

### 总体数据流

```
双击 McPack.exe
   │
   ├─ 1. Container.Init()        最早期 mmap 自身 exe，尾部扫描 EOCD，解析中央目录，
   │                             Store 校验，建虚拟目录表（不落盘）
   ├─ 2. JIT-safety 预热          在第一个 detour 安装前编译全部热路径
   ├─ 3. AutoDetectVersionId()   自动扫描 versions/ 目录，发现版本 ID
   ├─ 4. BuildFromVersionJson()  解析 version.json → 动态读取 mainClass、assetIndex
   ├─ 5. SyncFromManifest()      读取 .mcpack-sync 清单，按需同步到 game/
   ├─ 6. FakeFileSystem.Init()   安装 19 个 ntdll hook + 8 个 kernelbase/kernel32 托管 detour
   ├─ 7. JNI_CreateJavaVM        进程内创建 JVM；jvm.dll 优先真实磁盘加载，
   │                             缺失时经假 SEC_IMAGE 从容器内存加载
   └─ 8. McLaunch.Run()          构建 Z:\ 类路径 → natives 虚拟化到
                                 Z:\cache\natives → gameDir 就绪 → Client.main(String[])
                                 → 等待主菜单
```

### 尾部 zip 容器（`Container.cs`）

- 宿主最早期 `CreateFileMappingW + MapViewOfFile` 把整个 exe 映射进内存（只读）。
- 从文件尾回看最多 64 KB + 22 B 扫描 EOCD（`0x06054b50`），校验 commentLen 与文件尾距离一致才接受。
- 手动解析中央目录，建立条目表。zip 顶层只有两棵：`openjdk/` 与 `minecraft/`。
- **Store 校验**：任何条目 `method != 0` 或 `compSize != uncompSize` 都视为容器损坏，打印错误并以退出码 **100** 拒绝启动。运行时不做任何解压，全部按偏移直读。
- 损坏到无法解析时以退出码 **101** 退出。

### ntdll hook 虚拟文件系统（`FakeFileSystem.cs` + `native_hooks/`）

19 个 hook，分五组：

| 组 | Hook |
|---|---|
| 文件 | `NtCreateFile` `NtOpenFile` `NtReadFile` `NtClose` `NtQueryInformationFile` `NtQueryAttributesFile` `NtQueryFullAttributesFile` `NtQueryVolumeInformationFile` `NtSetInformationFile` |
| 映射 | `NtCreateSection` `NtMapViewOfSection` `NtUnmapViewOfSection` `NtQuerySection` |
| 目录枚举 | `NtQueryDirectoryFile` `NtQueryDirectoryFileEx` |
| 句柄 | `NtDuplicateObject` |
| 写入与锁 | `NtWriteFile` `NtLockFile` `NtUnlockFile` |
| **kernelbase/kernel32 托管 detour** | `CreateFileW` `GetFinalPathNameByHandleW`(×2) `FindFirstFileExW` `FindNextFileW`(×2) `FindClose` `FindFirstFileW` `FindNextFileW` `GetVolumePathNameW` `GetVolumeInformationW` `GetDriveTypeW` `GetDiskFreeSpaceExW` |

关键机制：

- **`Z:\` 虚拟根**：JVM 与游戏的路径访问被改写到 `Z:\openjdk\...`、`Z:\minecraft\...`，由容器条目表直接服务。
- **假句柄表**：`NtCreateFile`/`NtOpenFile` 对容器内文件与虚拟 natives 文件返回伪造句柄（文件 `0x5100xxxx`、section `0x52000000|n`），真实句柄一律放行到原 trampoline。
- **25H2 direct-syscall 兼容**：`kernelbase!CreateFileW` 和 `kernelbase!ReadFile` 在 Windows 25H2 上使用 direct syscall 绕过 ntdll hook。对此，`CreateFileW` 托管 detour 直接创建假句柄（容器 jar 文件、虚拟 natives 文件），或重写路径到物化真实磁盘文件（JDK conf 文件如 `java.security`）；`GetFinalPathNameByHandleW` 托管 detour 对假句柄直接返回 `Z:\` 路径，解决 JDK 21+ `toRealPath` 的 `FileSystemException`；`FindFirstFileExW`/`FindNextFileW`/`FindClose` 托管 detour 提供虚拟目录枚举，解决 JDK 24+ 在 25H2 上目录枚举 direct syscall 绕过 `NtQueryDirectoryFile` 的问题；`FindFirstFileW`/`FindNextFileW`(kernel32) 额外挂钩防止 `kernel32` 非 forwarder 实现绕过 `kernelbase` 钩子。
- **PHASE19 修复**：① `FindFirstFile` 系无匹配时显式设置 `ERROR_FILE_NOT_FOUND`，不再让 JDK 捡到线程残留错误码（残留 18 曾表现为 `FileSystemException ... 没有更多文件`，残留 0 曾让 `JDK_Canonicalize` 判定不可容忍导致 `jimage file name is null` 崩溃）；② `CreateFileW` 对 `Z:\cache\natives\` 提供与 `NtCreateFile` 同机制的虚拟文件句柄（java.io 链），JNA `File.createTempFile`/`FileOutputStream` 可用；③ `GetVolumePathNameW`/`GetVolumeInformationW`/`GetDriveTypeW`/`GetDiskFreeSpaceExW` 对 `Z:` 卷虚拟化（`Files.getFileStore` 可用，剩余空间取宿主 exe 所在卷真实值）；④ 修正 `FindFirstFileExW` 托管 detour 的 ABI（`lpFindFileData` 是第 3 参，此前误排到第 6 位导致写 0x0 访问违例）。
- **虚拟可写区**：仅 `Z:\cache\natives\` 子树可写，natives 提取与运行期 JNA/LWJGL/Netty 写入全走内存虚拟文件表，其余 `Z:\` 保持只读。
- **假 `SEC_IMAGE`**：`NtCreateSection` / `NtMapViewOfSection` 对容器内 PE 文件（如 `jvm.dll`）做纯托管 PE32+ 解析 + 手工镜像布局，内存里按节加载，不落盘。
- **JNI 进程内 JVM**：加载 `jvm.dll` → `JNI_CreateJavaVM` → 在宿主进程内直接跑 Minecraft。类路径全部是 `Z:\...` 虚拟路径。
- **native 守卫 stub**（`native_hooks/`，C）：每个 hook 有一个 `Stub_*` 前置分流，只在命中假句柄 / `Z:\` 路径时才进托管，GC 内部的 ntdll 调用不经过托管 hook。

### NativeAOT 与 native 守卫库

- 宿主以 `dotnet publish -c Release -r win-x64`（`<PublishAot>true</PublishAot>`）发布为纯原生单 exe，运行时零 JIT。
- native 守卫库以 `mcpack_hooks_static.lib` 通过 `<DirectPInvoke Include="__Internal">` + `<NativeLibrary>` 静态链接进 exe。
- JIT 调试模式（`Build` target）使用 `mcpack_hooks_shared.dll` 动态加载。
- MinHook 使用嵌入的修改后 MinHook.NET（新增 `CreateHook(IntPtr, IntPtr)` 原生 detour 重载），见 `third_party/Minhook.NET/`（BSD-3）。

### 版本自动检测（`McLaunch.AutoDetectVersionId`）

启动时自动扫描 `minecraft/versions/` 目录，找到第一个含匹配 `.json` 文件的版本目录，从中读取 `mainClass`（自动转换 `.` → `/` 适配 JNI 格式）和 `assetIndex`。无需任何硬编码，任意版本开箱即用。

### 同步清单（`McLaunch.SyncFromManifest`）

启动时读取容器内的 `.mcpack-sync` 清单文件，按其中列出的路径将文件从只读容器复制到可写 `game/` 目录。仅复制不存在的文件，已有文件永不覆盖。无清单时静默跳过。

### 退出码

| 码 | 含义 |
|---|---|
| `0` | 检测到游戏窗口（主菜单在渲染） |
| `3` | 游戏自行退出 |
| `42` | watchdog 超时（180 s 未检测到窗口） |
| `100` | 容器含非 Store 条目，拒绝启动 |
| `101` | 容器 zip 结构损坏 / 解析失败 |

---

## 构建

### 前置条件

- **.NET 10 SDK**（目标 `net10.0`，NativeAOT publish）
- **CMake**（PATH 中，或 VS 内置 CMake）
- **Visual Studio 构建工具**（MSVC，编译 native_hooks 与 AOT link）

### 构建启动器（NUKE 管线）

```powershell
# 首次构建：配置 CMake + 编译 native 库
cmake -S native_hooks -B native_hooks/build
.\build.ps1 Native

# 完整交付：Publish AOT + Pack 容器 + Append 尾部拼接
.\build.ps1 Append

# 仅 JIT 调试构建
.\build.ps1 Build

# 帮助
.\build.ps1 --help
```

NUKE Target 说明：

| Target | 作用 |
|---|---|
| `Build` | `dotnet build -p:PublishAot=false` 主工程，JIT 调试链路 |
| `Native` | `cmake --build native_hooks/build` 产 `mcpack_hooks_shared.dll` |
| `Pack` | `Minecraft/` 数据树 → `artifacts\container.zip`（全 Store 不压缩） |
| `Publish` | `dotnet publish -c Release -r win-x64` 产 NativeAOT 单 exe |
| `Append` | **最终交付**：Publish + Pack 后，把 zip 追加到 AOT exe 尾部 |

### 构建打包器

```powershell
# 发布打包器为单文件 exe（NativeAOT）
dotnet publish McPack.Packager -c Release -r win-x64
```

---

## 目录结构

```
McPack/
├─ build.ps1                        # NUKE 构建引导脚本
├─ LICENSE                          # GNU GPL v3
├─ McPack.slnx                      # 解决方案
├─ McPack/                          # 启动器主工程（net10.0 / NativeAOT）
│  ├─ Program.cs                    # 入口：容器 Init → 预热 → hook 安装 → JNI → MC 启动
│  ├─ Container.cs                  # 尾部 zip 容器：mmap + EOCD + 手动解析 + Store 校验
│  ├─ FakeFileSystem.cs             # ntdll hook VFS：Z:\ + 假句柄 + 假 SEC_IMAGE + 19 hooks
│  ├─ McLaunch.cs                   # 启动链：自动检测版本 → version.json → 同步清单 → Client.main
│  └─ Minecraft/                    # 游戏数据树（打包进容器）
├─ McPack.Packager/                 # GUI 打包器（WinForms / NativeAOT）
│  ├─ Program.cs                    # 入口
│  ├─ MainForm.cs                   # 主界面：目录选择、同步清单、进度条、日志
│  └─ PackagerEngine.cs             # 打包引擎：扫描 → 创建 Store zip + 同步清单 → 拼接 exe
├─ native_hooks/                    # C 守卫 stub 库（CMake）
│  └─ src/ntdll_hooks.c             # Stub_* 前置分流 + Suppress + SetCallbacks 绑定
├─ build/                           # NUKE 构建管线（Build.cs）
├─ third_party/Minhook.NET/         # 嵌入的修改后 MinHook.NET（BSD-3）
├─ artifacts/                       # 交付物 + gameDir
│  ├─ McPack.exe                    # 最终交付物（AOT 宿主 + 尾部 zip）
│  ├─ container.zip                 # 打包中间产物
│  └─ game/                         # gameDir（存档 / mods / config 持久）
└─ logs/                            # 运行日志
```

---

## 已知限制

- **gameDir 是真实目录**：存档/配置持久保存是特性；容器内打包的 mods 等不会自动进入 gameDir，需在打包器 GUI 中勾选同步清单。
- **natives 完全虚拟化**：natives 提取与运行期 JNA/LWJGL/Netty 写入全走虚拟 `Z:\cache\natives\` 内存区，真实 `game\cache` 零 natives、`%TEMP%` 零残留。
- **离线单机**：Realms 登录、微软账号、多人联机登录均不可用；离线身份由 exe 文件名决定。
- **仅 Windows x64**：ntdll hook 与 VFS 机制深度绑定 Windows 内核接口。Windows 25H2 上 `kernelbase` 部分 API（`CreateFileW`、`ReadFile`、`FindFirstFileExW`、`GetFinalPathNameByHandleW`）使用 direct syscall 绕过 ntdll hook，已通过额外的 12 个 kernelbase/kernel32 托管 detour 兼容。

---

## 许可证

GNU GPL v3（主工程 + native 守卫库）。MinHook.NET 部分为 BSD-3。
