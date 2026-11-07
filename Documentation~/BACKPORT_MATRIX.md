# v0.5.1 -> MCP2UNITY 0.5.2 backport matrix

| Subsystem | Base retained | Change in this branch |
| --- | --- | --- |
| PowerShell listener and ngrok process | v0.5.1 | Maintains original lifecycle, with native image and JSON envelope handling |
| Unity IPC request pump and heartbeat | v0.5.1 | Only response envelope, request expiry and interrupted-request safety patched |
| 21 legacy tool implementations | v0.5.1 | Preserved; extended dispatch only handles unknown legacy cases |
| Extended catalog and dispatch | v0.6.0 | Imported; 17 names appended to existing catalog |
| Runtime inspection | v0.6.0 | `unity_inspect_runtime` |
| View capture | v0.6.0 | `unity_capture_view`; JPEG over native MCP image block |
| Reviewed scene/script changes | v0.6.0 | `unity_propose_change_set`, `unity_get_change_sets`, `unity_propose_change_set_rollback` |
| Trace sampling | v0.6.0 | `unity_start_trace`, `unity_read_trace`, `unity_stop_trace` |
| VRChat diagnostics | v0.6.0 | `unity_validate_vrchat` |
| Asset reverse references | v0.6.0 | `unity_find_asset_references` |
| Runtime assertions | v0.6.0 | `unity_check_runtime` |
| Editor profiler sampling | v0.6.0 | `unity_start_profiler`, `unity_read_profiler`, `unity_stop_profiler` |
| Locally reviewed Editor and tests | v0.6.0 | `unity_propose_editor_action`, `unity_get_editor_actions`, `unity_get_test_results` |
| Permissions and local review UI | v0.6.0 | Opt-in for scene changes / Editor actions; manual approval only |
| Path security | v0.6.0 | Canonicalized and checked Assets/package paths |
| Edit Mode test fixtures | v0.6.0 | Adapted package path and expected version for MCP2UNITY |

**Not copied wholesale from v0.6.0:** changed PowerShell listener/HTTP processing, v0.6.0 full IPC replacement, new companion process lifecycle, full legacy executor refactor, and unverified full-stack reliability claims.

## Acceptance gates

1. UPM package imports in Unity 2022.3.22f1 without assembly or dependency errors.
2. MCP initialization and all 38 tool catalog schemas are listed, with existing 21 legacy behaviors unchanged.
3. Companion survives script compilation and Unity domain reload.
4. Capture returns a JPEG image block, and JSON arrays/null maintain their shape.
5. Locally approved changes, rollback, Editor actions and Edit Mode tests are verified in a disposable test project.
6. Regression test coverage for the specific v0.6.0 error is added once its reproducible log is available.

No item is claimed as passed until tested on the target Windows + Unity setup.

## Package-aware parity backport

- v0.6.0-style script discovery now uses Unity AssetDatabase registered package paths, including Git-installed PackageCache files, while retaining v0.5.1 declaration/reference classifications.
- Selected folder analysis can read registered `Packages/` folders when **Allow package scripts** is explicitly enabled; writes remain limited to `Assets/`.
- Unity Console reflection prefers the message field and supports v0.6.0 classification flags.
- Companion status JSON is read with atomic-file-friendly sharing.
- EditMode test fixtures permit pristine untitled Test Runner scenes, and test results retain individual skipped reasons.
- These are selective backports. The v0.5.1 PowerShell listener, companion lifecycle, and core IPC architecture are retained.
