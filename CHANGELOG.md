# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial repository scaffold: `slnx` solution, multi-targeted (`net8.0;netstandard2.0`)
  library project, and an offline xUnit test project.
- Centralized configuration: `Directory.Build.props` (nullable, latest C#, XML docs,
  warnings-as-errors, build-enforced code style), `Directory.Packages.props` (Central
  Package Management), `.editorconfig`, and a pinned SDK via `global.json`.
- Package metadata, deterministic builds, and SDK-implicit SourceLink with symbol packages.

[Unreleased]: https://github.com/gberikov/Mailgunner/commits/master
