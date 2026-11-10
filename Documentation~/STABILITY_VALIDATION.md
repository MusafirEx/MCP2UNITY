# MCP2UNITY v0.5.2 — Windows Companion and ngrok stability validation

This is an **additional transport validation track**, separate from **120/120 Unity automated tests** verified on 2026-11-10. These checks do not edit gameplay scenes or auto-restart services.

## Live results captured through ChatGPT → MCP → Windows Companion → Unity

On 2026-11-10, a connected Windows Unity 2022.3.62f3 Editor answered **12 consecutive read-only tool calls**: three each of `unity_get_status`, `unity_get_compilation_status`, `unity_get_active_scene` and `unity_get_project_info`. All **12/12** returned expected results.

- Measured call times: **401–1096 ms**; mean **626 ms**.
- Companion PID unchanged; ngrok reported `Ready` before and after.
- The gameplay scene remained clean with its original five root objects.
- Console and compilation reported no errors.

This verifies a short live burst, **not** continuous uptime, service restart recovery or real ngrok HTTP reachability. A `Ready` status alone does not establish that public HTTPS requests succeed.

## Reliability fix and function tests (Windows offline regression PASSED)

`Companion~/wdmcp-companion.ps1` now uses .NET `File.Replace` with an explicit unique `.bak-<GUID>` backup path for existing JSON destinations, and `File.Move` for new ones, with bounded retries and cleanup. This avoids both the prior delete-then-move missing-file gap and a Windows PowerShell 5.1 error when passing `$null` as the backup path.

`Tests~/test-companion-functions.ps1` now tests file replacement while a reader remains open, checks complete JSON/temporary-file cleanup, and exercises fresh/stale heartbeat recovery.

**Verified on the user's Windows host (2026-11-10):** the updated `Tests~/test-companion-functions.ps1` completed successfully: heartbeat, JSON `[]`, array of objects, array of numbers and `null`, MCP image content, baseline overwrite, 18 atomic file replacements while a read handle remained open, cleanup of `.tmp-*` and `.bak-*`, stale heartbeat detection and fresh heartbeat recovery all printed PASS. The test is offline and does not prove the running Companion has adopted the patched script. A running Companion continues using its previous loaded script until restarted after the UPM update.

## Procedure A — isolated Windows PowerShell regression

After updating UPM from repository `main`, open Windows PowerShell 5.1 in the package root and run:

    & '.\Tests~\test-companion-functions.ps1'

Expected: the offline checks print `PASS` and exit without throwing. The runner uses a disposable Windows Temp directory and does not stop the active Companion.

## Procedure B — real MCP local and optional public tunnel handshake

With Unity and Companion running, from the updated package root:

    & '.\Tests~\test-companion-stability.ps1' -ProjectRoot 'C:\path\to\MuOnBaAr' -Iterations 12

The script reads the **private** local endpoint from the project's Companion status JSON **in memory**; it never prints endpoint secrets. Each loop exercises JSON-RPC initialize, tools/list (47), ping, and a real `unity_get_status` call, and validates Unity heartbeat and unchanged Companion PID.

For an actual HTTPS ngrok MCP round trip, add `-TestPublicTunnel`:

    & '.\Tests~\test-companion-stability.ps1' -ProjectRoot 'C:\path\to\MuOnBaAr' -Iterations 12 -TestPublicTunnel

Do not share logs or screenshots containing private endpoint URLs or capability tokens.

## Procedure C — observe a manual restart

Save Unity scenes and clear pending Change Sets first. Run:

    & '.\Tests~\test-companion-stability.ps1' -ProjectRoot 'C:\path\to\MuOnBaAr' -Iterations 3 -TestPublicTunnel -ObserveRestart -RestartWindowSeconds 120

When the script says `Restart observation armed`, click **Restart Companion** in the Unity MCP Bridge window (or Stop then Start). The script itself **never restarts anything**. Pass criteria: the Companion PID changes, Unity heartbeat is fresh, and the local and selected public HTTPS JSON-RPC handshakes succeed after restart. If ngrok assigns a new URL, reconnect the ChatGPT app.

## Procedure D — Unity domain reload

Save scenes and confirm no pending Change Sets. Trigger a normal Editor domain reload (e.g. a controlled UPM refresh), then repeat Procedure B after Unity finishes importing. Check that Companion remains available, interrupted operations are not silently replayed, and gameplay scene remains unchanged. A temporary MCP error during reload is expected; correct recovery is the criterion.

## First direct transport soak on Windows — partial result (2026-11-10)

- Local MCP JSON-RPC handshake **PASSED** with 47 tools and bridge v0.5.2.
- Actual public ngrok HTTPS MCP JSON-RPC handshake **PASSED** with 47 tools.
- Ten of twelve requested local soak iterations completed (measured iteration times: 356, 265, 364, 381, 256, 341, 366, 354, 277 and 270 ms). At iteration 11, the observer threw `Missing file: companion-status.json`; **the 12-iteration soak did not pass**.
- A subsequent remote status/scene check succeeded; Windows Companion PID remained **20996**, ngrok reported `Ready`, Unity IPC connected and the scene remained clean with five original roots. This supports a transient status-file visibility gap rather than a confirmed lasting Companion crash.
- The running process had the **same PID observed before the atomic-file patch**, so the patch may not yet have been adopted by the live Companion. Restart the Companion locally after UPM refresh, confirm a **new PID**, then repeat the exact smoke test. Do not suppress or classify a missing status file as a passing iteration.
- Record separately the follow-up full soak result and post-restart HTTP/ngrok handshake. Do not infer public endpoint persistence from `Ready` alone.

## Status and exit criteria

| Test | Current evidence |
| --- | --- |
| Unity automation Phases 1–7 | **120/120 passed** |
| Live short connector burst | **12/12 passed** |
| Modified PowerShell function regression | **PASSED 2026-11-10 on Windows PowerShell 5.1**: all reported assertions including JSON `null`, native MCP image, 18 held-reader atomic replacements, temp/backup cleanup and heartbeat recovery |
| Repeated local MCP handshake script | **Not yet run on Windows** |
| Actual public ngrok HTTPS round trip | **Not yet run after patch** |
| Unity Editor domain reload with existing running Companion | **Passed on 2026-11-10** (Editor Action `27efd2959d84` accepted; Editor.log confirms public API compilation request, successful Tundra build and MonoManager domain reload; Companion PID 20996 unchanged; ngrok `Ready`; Unity IPC and 47 tools responsive; scene clean with 5 original roots) |
| Windows Companion restart after PowerShell patch | **Not yet tested after patch** |

Do not claim full Windows/ngrok transport certification until the outstanding checks have been run and inspected. Share only sanitized failure details, never private endpoints or ngrok credentials.
