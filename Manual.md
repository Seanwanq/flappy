# 🐦 Flappy User Manual (v0.2.0)

Flappy is a minimalist build system and package manager for modern C++ development. It automates compilation, linking, dependency fetching, and recursive builds via a simple `flappy.toml` file.

---

## 🚀 Core Commands

| Command | Description | Example |
| :--- | :--- | :--- |
| `flappy init [name]` | Start interactive wizard to create a new project | `flappy init my_app` |
| `flappy build [target]`| Compile the project (Debug by default) | `flappy build` |
| `flappy build --release`| Compile in Release mode | `flappy build --release` |
| `flappy run [target]` | Build and run the project | `flappy run` |
| `flappy test [target]`| Build and run automated unit tests | `flappy test` |
| `flappy profile add` | Interactively add a custom build profile | `flappy profile add` |
| `flappy compdb` | Generate `compile_commands.json` for LSP | `flappy compdb` |
| `flappy clean` | Remove build artifacts (`bin/`, `obj/`, `dist/`) | `flappy clean` |
| `flappy add <name>` | Add a remote or local dependency | `flappy add fmt --git <url>` |
| `flappy sync` | Force sync dependencies and update `flappy.lock` | `flappy sync` |

---

## 🛠 Project Configuration (`flappy.toml`)

Flappy uses a hierarchical configuration system. Values are inherited and merged from top to bottom.

### 1. Base Configuration (`[build]`)
Defines project-wide engineering decisions:
```toml
[build]
language = "c++"
standard = "c++20"
output = "bin/my_lib"
type = "lib" # exe, lib, dll
```

### 2. Profiles & Targets
Define how to build for specific environments.

#### 2.1 Platform Overrides
Automatically applied based on the host OS:
```toml
[build.windows]
compiler = "cl"
arch = "x64"

[build.linux]
compiler = "/usr/bin/g++"
arch = "arm64"
```

#### 2.2 Custom Profiles
Run with `flappy build <name>` or `flappy build -t <name>`:
```toml
[build.ci]
defines = ["CI_RUNNER"]
flags = ["-Werror"]

# Profiles can also have platform-specific implementations!
[build.ci.linux]
compiler = "clang++"
```

---

## 📦 CMake Integration

Flappy is designed to be a "good citizen" in the C++ ecosystem.

### 1. Consuming CMake Projects
If a dependency contains a `CMakeLists.txt`, Flappy automatically invokes CMake to build it and links the results. It ensures ABI compatibility by passing your project's compiler through to CMake.

### 2. Exporting to CMake
When you build a library (`type = "lib"` or `dll`), Flappy generates a `dist/` directory:
```text
dist/
  include/          # Public headers
  lib/              # Compiled libraries (.lib, .a, .so)
  cmake/
    MyLibConfig.cmake # Standard CMake package config
```

#### How to use a Flappy library in CMake:
In your `CMakeLists.txt`:
```cmake
list(APPEND CMAKE_PREFIX_PATH "path/to/flappy_project/dist")
find_package(MyLib REQUIRED)
target_link_libraries(your_app PRIVATE MyLib)
```

---

## ✨ Advanced Features

### 1. Automatic Compilation Database
Flappy generates `compile_commands.json` on every build/run. This provides **VS Code**, **Vim**, and **Zed** with perfect IntelliSense, including for headers located deep in the Flappy dependency cache.

### 2. Smart Configuration Interceptor
If you try to build on a platform that isn't configured in your `flappy.toml`, Flappy will catch it and offer to guide you through an interactive setup wizard instead of failing.

### 3. Recursive Dependencies
Flappy projects understand each other. If `Project A` depends on `Project B`, Flappy will recursively build `Project B` and correctly propagate all transitive include paths and libraries.
