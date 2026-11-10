# Changelog

## Phase 4 — Unity uGUI PlayMode tests (verified 2026-11-10)

- Added a separate PlayMode test assembly with 13 runtime interaction tests covering Button, Slider, Toggle, InputField, ScrollRect, and GraphicRaycaster input.
- PlayMode test fixtures use disposable Canvas/EventSystem objects and avoid saving gameplay scene changes.
- Made generated Toggle root UI image transparently raycastable across its entire row, including label area, and extended an existing EditMode assertion.
- Baseline before Phase 4: **73/73 EditMode tests passed** in live Unity 2022.3.62f3, with 47 MCP tools advertised.
- **Verified 13/13 PlayMode tests passed** in Unity 2022.3.62f3 on 2026-11-10 (Unity MCP Editor action `f197a999ff82`, run `9fd24057-5d0d-4cc2-9faf-78b657501b4f`). 0 failed/skipped/inconclusive; compilation and Console reported no errors/warnings. Combined automated count: **86/86** (73 EditMode + 13 PlayMode). These fixture tests are not a substitute for end-to-end PlayMode testing of persistent events on MCP-created UI.

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
