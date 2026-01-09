# Flappy 🐦

Flappy is a modern, lightweight, and fast C/C++ package manager and build tool written in F#. It provides a **Cargo-like experience** for C++ developers, replacing complex CMake or Makefile configurations with a simple, human-readable `flappy.toml`.

👉 **[View Full User Manual (Manual.md)](Manual.md)**

## ✨ Features

-   **Zero-Config Build**: Automatically detects MSVC, GCC, and Clang.
-   **Smart Dependencies**: Pull dependencies directly from Git or URLs.
-   **Automated Testing**: Standardized `flappy test` command for unit tests.
-   **Editor Integration**: Auto-generates `compile_commands.json` for precise code completion.
-   **Hierarchical Profiles**: Define custom build targets like `[build.arm64]` with platform overrides.
-   **Native AOT**: The tool itself is compiled to a tiny native binary with instant startup.
-   **Visual Studio Integration**: Auto-locates VS installations and configures the environment.
-   **Cross-Platform**: Works seamlessly on Windows, Linux, and macOS.

## 🚀 Getting Started

### Installation

If you have [Just](https://github.com/casey/just) and the [.NET SDK](https://dotnet.microsoft.com/download) installed:

```bash
git clone https://github.com/your-username/flappy.git
cd flappy
just install
```

### Create a New Project

```bash
flappy init my_project
cd my_project
flappy run
```

## 📦 Dependency Management

Add dependencies to your `flappy.toml`:

```toml
[dependencies]
# From Git
fmt = { git = "https://github.com/fmtlib/fmt", tag = "11.0.2" }

# Single Header from URL
stb_image = { url = "https://raw.githubusercontent.com/nothings/stb/master/stb_image.h" }

# Local path
my_lib = { path = "../my_lib" }
```

## 🛠️ Usage

-   `flappy init [name]`: Start the interactive project wizard.
-   `flappy build [profile]`: Build the current project (or a specific profile).
-   `flappy run [profile]`: Build and run the project executable.
-   `flappy test [profile]`: Build and run tests.
-   `flappy compdb`: Manually generate `compile_commands.json`.
-   `flappy cache clean`: Clear the global dependency cache.

## 📄 Configuration (`flappy.toml`)

Flappy uses a layered configuration system for maximum flexibility:

```toml
[package]
name = "hello_world"
version = "0.1.0"

[build]
language = "c++"
standard = "c++20"
output = "bin/hello"
type = "exe"

[build.windows]
compiler = "cl"
arch = "x64"

[build.linux]
compiler = "g++"
arch = "arm64"
```

## 🏗️ Project Structure

```text
my_project/
├── flappy.toml      # Project configuration
├── src/             # Source files (.cpp)
│   └── main.cpp
└── packages/        # Managed dependencies (linked automatically)
```

## 📜 License

MIT
