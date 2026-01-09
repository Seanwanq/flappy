# 🐦 Flappy User Manual (v0.2.0)

Flappy is a minimalist build system and package manager for modern C++ development. It automates compilation, linking, dependency fetching, and recursive builds via a simple `flappy.toml` file.

---

## 🚀 Core Commands

| Command | Description | Example |
| :--- | :--- | :--- |
| `flappy init [name]` | Start an interactive wizard to create a new project | `flappy init my_project` |
| `flappy build [profile]`| Compile the project (Debug mode by default) | `flappy build` |
| `flappy build --release`| Compile in Release mode (optimizations enabled) | `flappy build --release` |
| `flappy run [profile]` | Build and run the project | `flappy run` |
| `flappy test [profile]`| Build and run automated tests | `flappy test` |
| `flappy compdb [profile]`| Generate `compile_commands.json` for editor LSP | `flappy compdb` |
| `flappy clean` | Remove build artifacts (`bin/` and `obj/`) | `flappy clean` |
| `flappy add <name>` | Add a remote or local dependency | `flappy add fmt --git <url>` |
| `flappy sync` | Force sync dependencies and update `flappy.lock` | `flappy sync` |
| `flappy cache clean` | Clear the global dependency cache | `flappy cache clean` |

---

## 🛠 Project Configuration (`flappy.toml`)

Flappy uses a powerful hierarchical configuration system. Values are merged from top to bottom.

### 1. Base Build Configuration
The `[build]` section defines common settings for all platforms:

```toml
[build]
language = "c++"         # c++, c
standard = "c++20"       # c++11, 14, 17, 20, 23
output = "bin/my_app"    # Output binary path
type = "exe"             # exe, dll, lib
defines = ["GLOBAL_MACRO"]
```

### 2. Platform & Profile Overrides
You can override any field based on the target platform or a custom profile name.

#### 2.1 Platform Overrides
Automatically applied based on your current OS:
```toml
[build.windows]
compiler = "cl"
arch = "x64"

[build.linux]
compiler = "g++"
flags = ["-Wall"]
```

#### 2.2 Custom Profiles
Define custom targets and run them via `flappy build <name>`:
```toml
[build.arm64]
arch = "arm64"

# You can even nest platform overrides inside profiles!
[build.arm64.linux]
compiler = "aarch64-linux-gnu-g++"
```

### 3. Automated Testing (`[test]`)
Configure your test runner:
```toml
[test]
sources = ["tests/*.cpp"]
output = "bin/test_runner"
defines = ["RUN_UNIT_TESTS"]
```
Run with `flappy test`. If the project is a `lib`, Flappy automatically links your library to the test executable.

### 4. Dependencies
```toml
[dependencies]
# 1. Local Flappy or CMake projects
my_lib = { path = "../my_lib" }

# 2. Git repositories
fmt = { git = "https://github.com/fmtlib/fmt", tag = "11.0.2" }

# 3. Single header-only files from URL
stb_image = { url = "https://raw.githubusercontent.com/.../stb_image.h" }
```

---

## ✨ Advanced Features

### 1. High Performance Build Engine
*   **Parallel Compilation**: Automatically utilizes multi-core CPUs.
*   **Incremental Builds**: Only re-compiles files that have changed.
*   **Smart Linking**: Skips linking if no inputs have changed.

### 2. IDE & Editor Support
Flappy automatically generates a **Compilation Database** (`compile_commands.json`) every time you build or run the project. This allows editors like **VS Code (clangd)**, **Vim**, and **Zed** to provide:
*   Precise code completion
*   Go-to-definition (including for external libraries)
*   Real-time error checking

### 3. Linux & Nix Support
Flappy is fully compatible with WSL and Nix environments. It automatically handles `libssl` dependencies internally and allows specifying exact compiler paths (e.g. `/home/user/.nix-profile/bin/g++`) in your configuration.

### 4. First-Class CMake Compatibility
Flappy can drive local CMake environments to build dependencies automatically, passing through your selected compiler to ensure ABI compatibility.
