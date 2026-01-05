# Flappy 🐦

Flappy is a modern, lightweight, and fast C/C++ package manager and build tool written in F#. It provides a **Cargo-like experience** for C++ developers, replacing complex CMake or Makefile configurations with a simple, human-readable `flappy.toml`.

## ✨ Features

-   **Zero-Config Build**: Automatically detects MSVC, GCC, and Clang.
-   **Smart Dependencies**: Pull dependencies directly from Git or URLs.
-   **Native AOT**: The tool itself is compiled to a tiny (~4MB) native binary with instant startup.
-   **Visual Studio Integration**: Auto-locates VS installations and configures the environment (no more manual `vcvarsall.bat`).
-   **Cross-Platform**: Works seamlessly on Windows, Linux, and macOS.
-   **Interactive Scaffolding**: Create a new project in seconds with `flappy init`.

## 🚀 Getting Started

### Installation

If you have [Just](https://github.com/casey/just) and the [.NET SDK](https://dotnet.microsoft.com/download) installed:

```bash
git clone https://github.com/your-username/flappy.git
cd flappy
just install
```

This will build Flappy as a native binary and install it to `~/.local/bin` (ensure this directory is in your `PATH`).

### Create a New Project

```bash
flappy init my_project
cd my_project
flappy run
```

This will create a new C++ project, compile the boilerplate code, and run it.

## 📦 Dependency Management

Add dependencies to your `flappy.toml`:

```toml
[dependencies]
# From Git
nlohmann_json = { git = "https://github.com/nlohmann/json", tag = "v3.11.2" }

# Single Header from URL
stb_image = { url = "https://raw.githubusercontent.com/nothings/stb/master/stb_image.h" }

# Local path
my_lib = { path = "../my_lib" }
```

Flappy automatically handles downloading, caching, and adding the correct `include` paths to your compiler.

## 🛠️ Usage

-   `flappy init [name]`: Start the interactive project wizard.
-   `flappy build`: Build the current project.
-   `flappy run`: Build and run the project executable.
-   `flappy cache clean`: Clear the global dependency cache.

## 📄 Configuration (`flappy.toml`)

```toml
[package]
name = "hello_world"
version = "0.1.0"
authors = ["Your Name"]

[build]
compiler = "cl"        # or g++, clang++
standard = "c++20"     # c++11, c++14, c++17, c++20, c++23
arch = "x64"           # x64, x86, arm64
type = "exe"           # exe, dll
output = "bin/hello"
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
