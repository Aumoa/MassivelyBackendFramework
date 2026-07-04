---
name: cpp-backend-style
description: Repository-local C++ Backend formatting, naming, CMake, CMake Presets, and IDE support rules. Use when editing C++ source/header files, CMakeLists.txt, CMakePresets.json, C++ tests, or Visual Studio/VS Code project support for the CppBackendServer area.
---

# C++ Backend Style

## Core Sources

Use these rules for C++ Backend work in this repository. They capture the reusable formatting and IDE-generation practices adopted from the reference C++ project, without depending on that project's paths or tools.

## Formatting

- Write repository files and generated documentation in English.
- Use 4 spaces for indentation. Do not introduce tabs.
- Use CRLF line endings on Windows-edited repository files.
- Use Allman braces: opening braces go on their own line for namespaces, types, functions, and control blocks.
- Always use braces for control flow, including single-line `if`, `for`, `while`, and `else` bodies.
- Keep one statement per line and one declaration per line unless a compact initializer is clearer.
- Prefer a maximum line length near 120 characters. Wrap argument lists and initializer lists before they become hard to scan.
- Keep includes at the top of the file. Put the primary project header first, then standard library headers, then platform headers guarded by platform `#ifdef`s.
- Use `.clang-format` as the source of truth for mechanical C++ formatting.

## Naming

Adapt Microsoft's recommended C# naming style to C++ code:

- Use PascalCase for types, concepts that model types, public methods, and non-local constants that behave like named API concepts.
- Use camelCase for local variables and parameters.
- Prefix private C++ member fields with `m_`; prefer `m_PascalCase` for new fields.
- Use descriptive names instead of abbreviations. Avoid Hungarian notation and type-encoding prefixes.
- Name boolean values with affirmative predicates such as `isReady`, `hasSnapshot`, or `shouldReconnect`.
- Keep namespaces short and stable. Existing namespaces, wire names, protocol ids, serialized field names, and compatibility constants may keep their established spelling when renaming would break protocol documentation, tests, or external callers.
- Do not mechanically rename stable public or wire-facing identifiers solely for style. Rename them only as part of an intentional compatibility-aware change.

## C# Style Rules To Mirror

When translating C# style expectations into C++ decisions, mirror these major rules:

- PascalCase for types and public members.
- camelCase for parameters and locals.
- Interface-like abstractions should be clearly named by role; use an `I` prefix only when the local C++ design deliberately mirrors a C# interface boundary.
- Prefer explicit types when they improve readability; use `auto` only when the type is obvious from the right-hand side or when it avoids noisy iterator/template spellings.
- Prefer object initializers or aggregate initialization when it keeps construction clear.
- Prefer expression simplicity over clever compression; split complex conditions into named local variables.
- Keep exception and error messages actionable and written in English.

## C++ Design

- Use RAII for ownership and cleanup. Avoid raw owning pointers.
- Keep hot-path allocations, locks, blocking calls, and control-plane calls out of Gateway data-plane loops.
- Keep platform-specific code isolated behind narrow helpers and guarded includes.
- Prefer standard library types and existing local helpers before adding dependencies.
- Keep protocol codecs deterministic, byte-order explicit, and covered by cross-language vectors when shared with C#.

## CMake And IDE Support

- Keep CMake target-based: use `target_sources`, `target_include_directories`, `target_compile_features`, `target_compile_options`, and `target_link_libraries` instead of global include or compile settings.
- List headers in target sources so Visual Studio, VS Code, and other IDEs show the full project tree.
- Use `source_group(TREE ...)` and target `FOLDER` properties for Visual Studio navigation.
- Use CMake Presets for IDE-friendly configure/build/test entry points. Include presets for command-line generators and Visual Studio when practical.
- Generated IDE files are for navigation and compile diagnostics. Normal validation remains CMake configure/build plus CTest.
- Do not hard-code machine-specific absolute paths in CMake files, presets, or generation scripts.
- If a project-generation helper becomes necessary, implement it as a repository-local tool that discovers projects from source metadata and emits CMake presets or IDE metadata using relative paths.

## Validation

After C++ style, CMake, or C++ source changes:

- Run `clang-format` over touched C++ files when available.
- Run a CMake configure/build using the relevant preset or existing build directory.
- Run CTest for C++ tests when BUILD_TESTING is enabled.
- If CMake or CTest cannot run on the current machine, report the blocker and still perform `git diff --check`.
