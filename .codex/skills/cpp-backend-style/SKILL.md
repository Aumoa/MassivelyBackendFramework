---
name: cpp-backend-style
description: Repository-local C++ Backend formatting, established naming, CMake, CMake Presets, and IDE support rules. Use when editing C++ source/header files, CMakeLists.txt, CMakePresets.json, C++ tests, or Visual Studio/VS Code project support for the CppBackendServer area.
---

# C++ Backend Style

## Core Sources

Use these rules for C++ Backend work in this repository. They capture the reusable formatting and IDE-generation practices adopted from the reference C++ project, while preserving the naming and API shape already established in `CppBackendServer`.

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

Follow the existing C++ Backend naming style rather than mechanically translating C# naming rules into C++:

- Use lower_snake_case for new C++ namespaces, types, functions, enum values, variables, parameters, and constants when working in the current `CppBackendServer` style.
- Prefix private C++ member fields with `m_`; keep the spelling after `m_` consistent with the surrounding C++ code, such as `m_impl`, `m_sync`, or `m_gateway_node_id`.
- Use descriptive names instead of abbreviations. Avoid Hungarian notation and type-encoding prefixes.
- Name boolean values so their meaning is affirmative and readable, but preserve concise protocol field names such as `ready`, `success`, `healthy`, and `shutting_down` when they match the wire model.
- Keep namespaces short and stable. Existing namespaces, wire names, protocol ids, serialized field names, and compatibility constants may keep their established spelling when renaming would break protocol documentation, tests, or external callers.
- Do not mechanically rename stable public or wire-facing identifiers solely for style. Rename them only as part of an intentional compatibility-aware change.
- Use PascalCase only when integrating with an external API, generated artifact, or cross-language boundary that already requires that spelling.

## C# Style Relationship

The repository-level C# style policy still applies to C# code. For C++ Backend code, mirror only the broad readability goals from the C# policy:

- Keep names consistent within their local language and module conventions.
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
