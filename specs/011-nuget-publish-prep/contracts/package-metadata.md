# Contract: Package Metadata (nuspec / nuget.org listing)

**Feature**: `011-nuget-publish-prep`

The published package's external contract is the set of metadata fields nuget.org requires
and renders. A produced `.nupkg` **conforms** iff every row below holds. Verify by
inspecting the generated `Mailgunner.nuspec` and `lib/` entries inside the `.nupkg`
(see quickstart.md).

## Required nuspec fields

| Element | Required value | FR | Status today |
|---------|----------------|----|--------------|
| `<id>` | `Mailgunner` | FR-003 | ✅ present (id available on nuget.org) |
| `<version>` | SemVer from `v*` tag (first: `0.1.0-preview.1`) | FR-008 | ⚠️ currently from `VersionPrefix` → switch to MinVer |
| `<authors>` | `Gany Berikov` | FR-003 | ✅ present |
| `<description>` | library description | FR-003 | ✅ present |
| `<license type="expression">` | `MIT` | FR-004 | ✅ present |
| `<projectUrl>` | `https://github.com/gberikov/Mailgunner` | FR-003 | ✅ present |
| `<repository url/commit/branch>` | git, deterministic | FR-007 | ✅ present (SourceLink) |
| `<tags>` | `mailgun sinch email transactional-email smtp mail` | FR-003 | ✅ present |
| `<readme>` | `README.md` | FR-004 | ✅ present |
| `<icon>` | `icon.png` | FR-020 | ❌ **missing — add** |
| `<releaseNotes>` | reachable CHANGELOG URL | FR-020 | ❌ **missing — add** |
| `<dependencies>` groups | only `System.Text.Json`, `Polly`, `Microsoft.Extensions.Http`; per TFM | FR-017 | ✅ present; MUST stay MinVer-free |

## Package payload

| Path in `.nupkg` | Required | FR |
|------------------|----------|----|
| `lib/net8.0/Mailgunner.dll` + `.xml` | yes | FR-006 |
| `lib/netstandard2.0/Mailgunner.dll` + `.xml` | yes (deps verified to restore) | FR-006 |
| `README.md` | yes | FR-004 |
| `icon.png` | yes | FR-020 |
| companion `.snupkg` with PDBs | yes | FR-007 |

## Rendering / trust requirements

- README links MUST resolve off-repository → absolute GitHub URLs for file links;
  anchors allowed (FR-013, SC-007).
- Non-affiliation notice MUST appear in README **and** package metadata (FR-005, SC-008).
- 100% of required fields present and accurate (SC-002).

## Non-conformance (fail the contract)

- A `MinVer` (or any build-only tool) entry appearing in a `<dependencies>` group → FR-017
  violation.
- A hard-coded `<version>` not derived from the tag → FR-008 violation.
- A relative file link in the packed README → FR-013 / SC-007 violation.
