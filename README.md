# Flappy 🐦

**Stop writing CMakeLists. Start coding C++.**

[中文版](README_CN.md)

Flappy is a modern, lightweight build system and package manager that brings the **Rust/Cargo experience** to C++. It eliminates the pain of dependency management, especially for legacy libraries like OpenSSL or FFmpeg.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## ✨ Why Flappy?

*   **Zero-Config Build**: No more `CMakeLists.txt` boilerplate. Just `flappy run`.
*   **"Escape Hatch" for Legacy Libs**: Use `build_cmd` to build *any* library (Make, Autotools, NMake) while keeping it managed.
*   **Smart Cross-Platform**: Define `[build.windows]` and `[build.linux]` overrides in one file.
*   **Automated Injection**: Flappy automatically injects header/lib paths into environment variables. `cl main.c` just works.
*   **Editor Ready**: Auto-generates `compile_commands.json` for VS Code / clangd.

## 🚀 Quick Start

```bash
# 1. Install (requires .NET 8+)
git clone https://github.com/your-username/flappy.git
dotnet build -c Release

# 2. Create Project
flappy init my_game -l c++ -s c++20

# 3. Add Dependencies
# (Example: Add fmt via git)
flappy add fmt --git https://github.com/fmtlib/fmt.git

# 4. Run!
flappy run
```

## 📦 Handling "Hard" Dependencies (Example)

How to consume a raw C library (like `zlib`) without headaches:

```toml
[dependencies.zlib]
git = "https://github.com/madler/zlib.git"
# Tell Flappy where the built lib ends up
libs = ["zlib.lib"]

# Windows build logic
[dependencies.zlib.windows]
build_cmd = "nmake -f win32/Makefile.msc"

# Linux build logic
[dependencies.zlib.linux]
build_cmd = "./configure && make"
libs = ["libz.a"]
```

## 🛠 Features

*   **Topological Build Order**: Automatically resolves dependency graphs.
*   **Artifact Isolation**: Separate caches for Debug/Release builds (ABI Safe).
*   **Incremental Builds**: Smart hashing skips expensive rebuilds.
*   **Manual Bridging**: Fix dependencies for libraries you don't own.

## 📜 License

MIT
