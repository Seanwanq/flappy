# 🐦 Flappy User Manual (v0.2.0)

Flappy is a minimalist build system and package manager for modern C++ development. It automates compilation, linking, dependency fetching, and recursive builds via a simple `flappy.toml` file.

---

## 🚀 Core Commands

| Command | Description | Example |
| :--- | :--- | :--- |
| `flappy init [name]` | Start an interactive wizard to create a new project | `flappy init my_project` |
| `flappy build` | Compile the current project (Debug mode) | `flappy build` |
| `flappy build --release`| Compile in Release mode (optimizations enabled) | `flappy build --release` |
| `flappy run [-- args]` | Build and run the project, passing extra arguments | `flappy run -- --port 80` |
| `flappy clean` | Remove build artifacts (`bin/` and `obj/`) | `flappy clean` |
| `flappy add <name>` | Add a remote or local dependency | `flappy add fmt --git <url>` |
| `flappy sync` | Force sync dependencies and update `flappy.lock` | `flappy sync` |
| `flappy cache clean` | Clear the global dependency cache | `flappy cache clean` |

---

## 🛠 Project Configuration (`flappy.toml`)

The `flappy.toml` file in your project root controls the entire build process:

```toml
[package]
name = "my_app"
version = "0.1.0"

[build]
compiler = "cl"          # Supports cl (MSVC), g++, clang++
language = "c++"         # Supports c++, c
standard = "c++17"       # c++11, 14, 17, 20, 23 (or c89, c99, c11, c17)
arch = "x64"             # x64, x86, arm64
type = "exe"             # exe, dll (dynamic), lib (static)
output = "bin/my_app"    # Output binary path
defines = ["DEBUG_MODE"] # Global macro definitions
flags = ["/W4"]          # Custom compiler flags

### 2.1 Cross-Platform Overrides
You can provide platform-specific overrides for any field in the `[build]` section:

```toml
[build]
standard = "c++20"

[build.windows]
compiler = "cl"
flags = ["/W4"]

[build.linux]
compiler = "g++"
flags = ["-Wall", "-pthread"]
```

Flappy automatically detects your OS and merges the corresponding section.

[dependencies]
# 1. Local Flappy or CMake projects (supports automatic recursive builds)
my_lib = { path = "../my_lib" }

# 2. Git repositories
nlohmann_json = { git = "https://github.com/nlohmann/json", tag = "v3.11.2" }

# 3. Single header-only files from URL
stb_image = { url = "https://raw.githubusercontent.com/.../stb_image.h" }
```

---

## ✨ Advanced Features

### 1. High Performance Build Engine
*   **Parallel Compilation**: Automatically utilizes multi-core CPUs to compile source files concurrently.
*   **Incremental Builds**: Only re-compiles files that have changed, based on file timestamps.
*   **Smart Linking**: Skips the linking stage entirely if no source files or dependency libraries have changed.

### 2. Powerful Dependency Management
*   **Recursive Building**: If a dependency contains a `flappy.toml` or `CMakeLists.txt`, Flappy will **automatically invoke** the appropriate build system before building the main project.
*   **Automatic Linking**: Flappy recursively searches dependency directories for `.lib` (Windows) or `.a/.so` (Unix) files and adds them to the linker arguments automatically.
*   **DLL Auto-Copy**: On Windows, Flappy automatically copies `.dll` files from dependencies to the executable's output directory after a successful build.

### 3. First-Class CMake Compatibility
Flappy doesn't force your dependencies to use Flappy. As long as a library supports CMake, Flappy can integrate it seamlessly by driving your local CMake environment.

### 4. Modern Logging & UX
Inspired by Cargo's logging style:
*   **Color-coded actions** (e.g., Compiling, Linking, Copying) in Green.
*   **Build timing** information accurate to 0.01s.
*   **Quiet Mode**: Automatically filters out verbose MSVC/Compiler banners, showing only what matters.

---

## 📂 Standard Project Structure

```text
my_project/
├── flappy.toml      # Project configuration
├── flappy.lock      # Dependency lockfile (auto-generated)
├── include/         # [Optional] Public headers (.h, .hpp)
├── src/             # Source code (.c, .cpp, .cc, .cxx)
│   ├── main.cpp
│   └── utils.cpp
├── bin/             # Build output (EXE, DLL, LIB, PDB)
└── obj/             # Intermediate object files
```

---

**Tip**: Run `just install` to install the latest version of Flappy to your system and enjoy a fast, modern C++ build experience anywhere!
