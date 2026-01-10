# 🐦 Flappy User Manual (v0.0.1)

Flappy is a modern build system and package manager for C++. It brings the ease of Cargo (Rust) to the C++ ecosystem.

## 📚 Quick Start

```bash
# 1. Create a new project
flappy init my_app

# 2. Add a dependency
flappy add fmt --git https://github.com/fmtlib/fmt.git

# 3. Build & Run
flappy run
```

---

## 🔧 The "Hard" Stuff: Managing Complex C Dependencies

Flappy shines where other tools struggle: integrating "Raw" libraries (like OpenSSL, FFmpeg) that don't use CMake or Flappy naturally.

### Scenario: "I need to use libcurl, which depends on OpenSSL"

Problem:
1.  `libcurl` source comes from git, has no `flappy.toml`.
2.  `openssl` source comes from git, has no `flappy.toml`.
3.  `libcurl` build needs `openssl` headers.
4.  On Windows `openssl` uses `nmake`, on Linux it uses `make`.

**Solution: The "Bridge" Config**

You define the relationship in your **Root** `flappy.toml`.

```toml
[package]
name = "my_app"
version = "0.1.0"

# 1. Define OpenSSL (The Leaf)
[dependencies.openssl]
git = "https://github.com/openssl/openssl.git"
tag = "openssl-3.2.0"
# Tell Flappy where the built .lib files end up (so they can be linked)
libs = ["libssl.lib", "libcrypto.lib"] 

# 1.1 Platform-Specific Build Commands
[dependencies.openssl.windows]
build_cmd = "perl Configure VC-WIN64A && nmake"

[dependencies.openssl.linux]
build_cmd = "./config && make"
libs = ["libssl.a", "libcrypto.a"] # Override libs for Linux

# 2. Define LibCurl (The Middleman)
[dependencies.libcurl]
git = "https://github.com/curl/curl.git"
# CRITICAL: Manually tell Flappy that libcurl depends on openssl
dependencies = ["openssl"] 
libs = ["libcurl.lib"]

[dependencies.libcurl.windows]
# CRITICAL: Use injected environment variable to find OpenSSL headers
# Flappy automatically sets %FLAPPY_DEP_OPENSSL_INCLUDE%
build_cmd = """
cmake . -DOPENSSL_INCLUDE_DIR="%FLAPPY_DEP_OPENSSL_INCLUDE%" 
        -DOPENSSL_ROOT_DIR="%FLAPPY_DEP_OPENSSL_ROOT%" 
        && cmake --build . --config Release
"""
```

### Key Concepts

#### 1. Platform Overrides
Use `[dependencies.pkg.windows]`, `[dependencies.pkg.linux]`, `[dependencies.pkg.macos]` to override:
*   `build_cmd`: The command to build the library.
*   `libs`: List of library files to link against.
*   `defines`: Preprocessor definitions.

#### 2. Environment Injection
When Flappy runs your `build_cmd`, it injects helpful variables:
*   `FLAPPY_DEP_<NAME>_INCLUDE`: Path to `include/` of dependency.
*   `FLAPPY_DEP_<NAME>_LIB`: Path to `lib/` of dependency.
*   `CC` / `CXX`: Path to your configured compiler.
*   `INCLUDE` / `LIB`: (MSVC) Automatically updated with dependency paths.
*   `CPATH` / `LIBRARY_PATH`: (GCC/Clang) Automatically updated with dependency paths.

#### 3. Bridging (`dependencies = [...]`)
If a library is "Raw" (no `flappy.toml`), you can specify its dependencies manually in the parent config using the `dependencies` list. This ensures Flappy builds them in the correct order and passes the metadata.

---

## 📖 CLI Reference

*   `init [name]`: Create project.
*   `build`: Build debug.
    *   `--release`: Build release.
    *   `--no-deps`: **(Advanced)** Skip dependency checks (internal use).
*   `run`: Build and run.
*   `test`: Run tests.
*   `clean`: Remove `bin` and `obj`.
*   `sync`: Resolve and install dependencies (updates `flappy.lock`).
*   `update [name]`: Update a specific dependency from source.
*   `compdb`: Generate `compile_commands.json` for IDEs.
