# 🐦 Flappy Full Reference Manual (v0.0.1)

[中文版](Manual_CN.md)

Flappy is designed to provide an "out-of-the-box" build experience for C++. This manual covers all features, configuration options, and advanced usage of Flappy.

---

## 🛠 Configuration File: `flappy.toml`

The core of your project. It consists of four main sections: `[package]`, `[build]`, `[test]`, and `[dependencies]`.

### 1. Project Metadata `[package]`
| Field | Description | Example |
| :--- | :--- | :--- |
| `name` | Project name (affects default output filename) | `"my_project"` |
| `version` | Project version | `"0.1.0"` |
| `authors` | List of authors (array) | `["Name <email@example.com>"]` |

---

### 2. Build Configuration `[build]`
This is where Flappy's power lies, supporting multi-level overrides.

#### Core Fields:
*   **`language`**: `"c"` or `"c++"`.
*   **`standard`**: Language standard, e.g., `"c++17"`, `"c++20"`, `"c11"`.
*   **`type`**: Output type: `"exe"`, `"lib"` (static library), `"dll"` (dynamic library).
*   **`output`**: Output path and filename, e.g., `"bin/app"`.
*   **`compiler`**: Compiler command, e.g., `"cl"`, `"g++"`, `"clang++"`.
*   **`arch`**: Target architecture: `"x64"`, `"x86"`, `"arm64"`.
*   **`defines`**: Array of preprocessor definitions, e.g., `["DEBUG", "VERSION=1"]`.
*   **`flags`**: Array of native compiler flags, e.g., `["/W4", "-O3"]`.

#### Hierarchical Overrides (Inheritance):
You can refine configurations for specific environments using suffixes. Merging priority is as follows (higher overrides lower):

1.  `[build]` (Base configuration)
2.  `[build.debug]` or `[build.release]` (Mode override)
3.  `[build.windows]` / `[build.linux]` / `[build.macos]` (Platform override)
4.  `[build.windows.debug]` (Platform + Mode combination override)
5.  `[build.target_name]` (Custom Profile override, see below)

**Example:**
```toml
[build]
standard = "c++20"
defines = ["GLOBAL"]

[build.debug]
defines = ["DEBUG_ONLY"] # Only active in debug mode

[build.windows]
compiler = "cl" # Default to cl on Windows

[build.windows.release]
flags = ["/O2"] # Only active in Windows Release mode
```

---

### 3. Dependency Management `[dependencies]`
Supports Git, URL, and local paths, and handles non-Flappy projects.

#### Defining Dependencies:
```toml
[dependencies.fmt]
git = "https://github.com/fmtlib/fmt.git"
tag = "10.2.1"

[dependencies.stb]
url = "https://example.com/stb_image.h"

[dependencies.mylib]
path = "../mylib"
```

#### Advanced Dependency Fields:
*   **`build_cmd`**: Custom build command. If present, Flappy calls this instead of automatic compilation.
*   **`dependencies`**: **(Bridging)** Manually specify what other dependencies this item depends on (for non-Flappy projects).
*   **`include_dirs`**: Manually specify header directories, e.g., `["include", "src/public"]`.
*   **`lib_dirs`**: Manually specify library search directories.
*   **`libs`**: Manually specify library filenames to link, e.g., `["zlib.lib"]`.
*   **`defines`**: Macros propagated to this dependency and its consumers.

#### Platform/Mode Overrides for Dependencies:
Supports `[dependencies.pkg.windows]`, `[dependencies.pkg.debug]`, etc., following the same hierarchy as `[build]`.

**Ultimate Example:**
```toml
[dependencies.openssl]
git = "..."
[dependencies.openssl.windows.debug]
build_cmd = "nmake -f Makefile.msvc"
libs = ["libssld.lib"]

[dependencies.libcurl]
git = "..."
dependencies = ["openssl"] # Declare that libcurl depends on openssl
```

---

## 🚀 Environment Injection

When Flappy runs a `build_cmd`, it automatically injects variables for your script to use:

| Variable | Description |
| :--- | :--- |
| `CC` / `CXX` | Path to the currently configured compiler |
| `FLAPPY_DEP_<NAME>_INCLUDE` | Path to the include directory of dependency `<NAME>` |
| `FLAPPY_DEP_<NAME>_LIB` | Path to the library files of dependency `<NAME>` |
| `INCLUDE` / `LIB` | (MSVC) System variables automatically appended with dependency paths |
| `CPATH` / `LIBRARY_PATH` | (GCC/Clang) System variables automatically appended with dependency paths |

*Note: `<NAME>` is converted to uppercase and dashes `-` are replaced with underscores `_`.*

---

## 💻 CLI Reference

### Basic Commands
*   **`flappy init [name]`**: Initialize a project in the current or a new directory.
*   **`flappy build [profile]`**: Execute build.
    *   `--release`: Switch to Release mode.
    *   `--no-deps`: **(Advanced)** Skip dependency checks (internal optimization).
    *   `-t, --target <name>`: Use specific configuration defined in `[build.<name>]`.
*   **`flappy run [profile] [-- <args>]`**: Build and run. Arguments after `--` are passed to the program.
*   **`flappy test [profile]`**: Build and run tests defined in the `[test]` section.
*   **`flappy sync`**: Resolve graph, download/build all dependencies, and update `flappy.lock`.
*   **`flappy clean`**: Remove build artifacts (`bin/`, `obj/`, `dist/`).

### Helper Commands
*   **`flappy compdb [profile]`**: Force generate `compile_commands.json`.
*   **`flappy profile add`**: Interactively add a custom build Profile.
*   **`flappy xplat`**: Interactively configure cross-platform toolchains.
*   **`flappy cache clean`**: Clear the global dependency cache.

---

## 🔄 Build Lifecycle

1.  **Resolution**: Parses `flappy.toml`, builds global dependency graph (DAG), detects cycles and version conflicts.
2.  **Fetching**: Downloads missing dependency sources.
3.  **Dependency Build**: Builds dependencies in topological order (leaf to root).
    *   For Flappy projects: Recursively calls `flappy build --no-deps`.
    *   For Raw projects: Calls `build_cmd`.
4.  **Main Build**: Compiles current project source and links all dependency artifacts.
5.  **Distribution**: Collects libraries and headers into `dist/`, and generates `CMake` config files.
