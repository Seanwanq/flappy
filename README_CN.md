# Flappy 🐦

**别再写 CMakeLists 了。开始写 C++ 吧。**

[English Version](README.md)

Flappy 是一个现代化、轻量级的构建系统和包管理器，旨在为 C++ 带来 **Rust/Cargo 的开发体验**。它消除了依赖管理的痛苦，特别是对于像 OpenSSL 或 FFmpeg 这样的遗留库。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## ✨ 为什么选择 Flappy?

*   **零配置构建**: 不再需要繁琐的 `CMakeLists.txt`。只需 `flappy run`。
*   **老旧库的“逃生舱”**: 使用 `build_cmd` 构建任何库 (Make, Autotools, NMake)，同时保持受管状态。
*   **智能跨平台**: 在一个文件里定义 `[build.windows]` 和 `[build.linux]` 覆盖。
*   **自动路径注入**: Flappy 自动将头文件/库路径注入环境变量。`cl main.c` 直接就能跑通。
*   **编辑器友好**: 自动为 VS Code / clangd 生成 `compile_commands.json`。

## 🚀 快速开始

```bash
# 1. 安装 (需要 .NET 8+)
git clone https://github.com/your-username/flappy.git
dotnet build -c Release

# 2. 创建项目
flappy init my_game -l c++ -s c++20

# 3. 添加依赖
# (例如: 通过 git 添加 fmt)
flappy add fmt --git https://github.com/fmtlib/fmt.git

# 4. 运行!
flappy run
```

## 📦 处理“硬核”依赖 (示例)

如何毫无压力地使用原生 C 库 (如 `zlib`)：

```toml
[dependencies.zlib]
git = "https://github.com/madler/zlib.git"
# 告诉 Flappy 构建出的库文件在哪
libs = ["zlib.lib"]

# Windows 构建逻辑
[dependencies.zlib.windows]
build_cmd = "nmake -f win32/Makefile.msc"

# Linux 构建逻辑
[dependencies.zlib.linux]
build_cmd = "./configure && make"
libs = ["libz.a"]
```

## 🛠 特性

*   **拓扑构建顺序**: 自动解析依赖图。
*   **产物隔离**: 针对 Debug/Release 构建分别缓存 (ABI 安全)。
*   **增量构建**: 智能哈希机制跳过不必要的重新构建。
*   **手动桥接**: 为你不拥有的库手动修复依赖关系。

## 📜 许可证

MIT
