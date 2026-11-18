# MCP2UNITY local image OCR

**Implementation on `main` (2026-11-19):** Adds MCP tool `unity_read_image_text` (49th advertised tool). **Unity Editor compilation and real-image end-to-end OCR are pending verification after a Git UPM update.** The earlier 237/237 EditMode result belongs to the previous 48-tool build.

## Engine and privacy

OCR is performed **locally with the bundled Tesseract executable on Windows x64, or an optional external fallback**. MCP2UNITY never uploads the image to an OCR API or downloads OCR models at runtime. The **extracted text is returned to the requesting MCP client** as ordinary tool output. On Windows x64, the published native bundle includes the engine and eng/msa/ind language packs; for fallback installations, install Tesseract OCR and its language data separately. Typical language codes: `eng` (English), `msa` (Malay), `ind` (Indonesian). The language must exist in the Tesseract installation; this package does not bundle any OCR binaries, fonts or traineddata.

On Windows: add `tesseract.exe` to PATH, or set the local executable path in **Window → Wasim Development → Unity MCP Bridge → Companion and tunnel settings → Tesseract OCR executable**. The default `tesseract` setting also checks standard `Program Files/Tesseract-OCR/tesseract.exe` locations. This is a local Editor preference, **not** an MCP argument; remote tool calls cannot choose an arbitrary executable or CLI flags.

## Portable Tesseract shipped inside Git UPM (Windows x64)

MCP2UNITY now **prefers the engine bundled in the resolved package** at `Tools~/OCR/win-x64/tesseract.exe`, with `tessdata/eng.traineddata`, `msa.traineddata`, and `ind.traineddata`. It passes `--tessdata-dir` explicitly and runs with `UseShellExecute=false`. Windows does **not** need an external Tesseract installation once the native folder is published in the branch/revision installed by UPM.

The native vendor files are built by [GitHub Actions](../.github/workflows/bundle-tesseract-windows.yml) with [Tools~/OCR/build_windows_bundle.py](../Tools~/OCR/build_windows_bundle.py), using the official MSYS2 UCRT64 Tesseract package, recursively detected DLL imports, traineddata language packages and captured third-party licenses. The workflow creates `bundle-manifest.json` containing SHA-256 for each packaged file and checks an isolated (no MSYS2 PATH) real-image OCR smoke test. The validated DLL, EXE and models are **committed to the same Git repository** for Unity Package Manager to retrieve. A failed build does not publish untested binaries.

The first supported native target is **Windows x64 Editor only**. Non-Windows Unity installations and revisions before the binary bundle has been committed retain the existing explicit/system-engine fallback. No binaries are downloaded or installed at Unity runtime. The local executable setting in Unity MCP Bridge remains available for fallback; when the bundled version exists, it takes priority.

**Deployment check:** verify the actual `Tools~/OCR/win-x64/tesseract.exe` and `bundle-manifest.json` are present on `main`. Merely adding a CI workflow does not ship an executable; successful CI completion and a UPM update to a revision containing the committed binaries are required.

## Supported sources

- `selected` (default): image file currently selected in the Unity **Project** window. The selection must resolve to a supported image asset under `Assets/`.
- `asset`: explicit relative path under `Assets/` (PNG, JPEG, BMP or TIFF; maximum 20 MB). No absolute paths, traversal, symlinks or network URLs.
- `scene`: captures the Scene View camera using the existing `unity_capture_view` renderer; **gizmos, labels and other editor overlays are not included**.
- `game`: captures the **focused Game View** (including its visible editor toolbar) using the existing pixel-copy mode; requires an open, focused Game View.
- `check`: verifies whether the local Tesseract executable can run. No image needed.

The Scene/Game capture is bounded to 64–1280 px per dimension and does not save images in project assets. Temporary captured images are stored in OS temp and normally deleted when the OCR request finishes.

## Example tool arguments

Check local engine:

```json
{"source":"check"}
```

Read currently selected image:

```json
{"source":"selected","language":"eng+msa","pageSegmentation":11}
```

Read an asset image:

```json
{
  "source":"asset",
  "path":"Assets/UI/ReferenceScreen.png",
  "language":"eng",
  "pageSegmentation":6,
  "maximumCharacters":8000
}
```

Read text on the focused Game View:

```json
{"source":"game","width":1280,"height":720,"language":"eng","pageSegmentation":11}
```

## Options and response

- `source`: `check`, `selected`, `asset`, `scene` or `game`.
- `path`: only for `asset`.
- `language`: default `eng`; one to three three-letter Tesseract language codes joined by `+` (e.g., `eng+msa`).
- `pageSegmentation`: `3` automatic, `6` single text block (**default**), `7` one line, `11` sparse text.
- `maximumCharacters`: 100–16,000 (**default 4,000**). `truncated=true` signals a clipped response.
- `width`/`height`: 64–1280, defaults 1280×720; used only for `scene` and `game`.

The JSON result includes `engine`, `source`, `image`, `language`, `pageSegmentation`, `text`, `lines`, `characterCount`, `hasText` and `truncated`. OCR is approximate: visual confirmation is recommended, particularly for code, UI labels, numbers and fine text.

A missing Tesseract executable returns an actionable error for OCR requests and `available:false` for `source=check`. Missing language packs also return actionable errors. No false positive recognition is generated. OCR is a read-only MCP tool and does not require Change Set approval.

## Validation

Added five disposable EditMode tests in `Tests/Editor/ImageTextReaderIntegrationTests.cs` for the 49-tool contract, read-only annotations, optional-engine check response, supported source/option validation, asset path security and selected-object safeguards. Updated historical 48-tool contract checks and the Windows companion stability test expectations to 49.

After installing the updated package: verify compilation, call `unity_get_status` (expect 49 tools), call `unity_read_image_text` with `{"source":"check"}`, then test a known image with installed `eng` data, and finally run the entire Unity EditMode suite. **These tests have not been executed against the new package in a connected Unity Editor yet.**
