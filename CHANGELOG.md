# Changelog

## 0.5.2 (MCP2UNITY experimental rebuild)

- Restored all 50 source files from upstream v0.5.1 without changing the baseline IPC/companion lifecycle.
- Added 13 modular v0.6.0 Editor source files with their corresponding original .meta files (17 new tools).
- Wired new catalog entries and dispatcher fallback while retaining original implementations for 21 legacy tools.
- Adopted v0.6.0 reviewed change-set permissions, Editor-action settings, path checks, and Test Framework assembly references.
- Added native MCP image envelope forwarding, JSON array/null preservation and a real Unity IPC self-test.
- Rejected expired requests and prevented automatic replay of interrupted in-flight operations after domain reload.
- Added six adapted Unity integration tests and Windows PowerShell companion regression tests.
- Assigned independent UPM package ID `com.musafirex.mcp2unity` and version 0.5.2.
- **Status:** Compiling in Unity and passing real Windows/Unity tests are still required; v0.6.0 defects are not yet root-caused.

## Original upstream changelog

See the original v0.5.1 release at https://github.com/wasim-development/wasim-unity-mcp-bridge/releases/tag/v0.5.1.
