using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WasimDevelopment.UnityMcpBridge
{
    /// <summary>
    /// Local-only OCR for project-owned image assets or an Editor view.
    /// No network calls, project modifications, arbitrary image paths or
    /// per-request executable/CLI arguments are accepted.
    /// Tesseract is an optional machine-local dependency.
    /// </summary>
    internal static class ImageTextReader
    {
        private const long MaxImageBytes = 20L * 1024L * 1024L;
        private const int OcrTimeoutMilliseconds = 18000;
        private const int VersionTimeoutMilliseconds = 3500;
        private static readonly string[] ImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"
        };

        internal static JObject Read(JObject args)
        {
            string source = (string)args["source"] ?? "selected";
            ValidateSource(source);
            string executable = ResolveExecutable();
            if (source == "check")
            {
                string version;
                bool available = TryGetVersion(executable, out version);
                return new JObject
                {
                    ["engine"] = "Tesseract (local only)",
                    ["available"] = available,
                    ["version"] = version,
                    ["supportedSources"] = new JArray("selected", "asset", "scene", "game"),
                    ["supportedImageExtensions"] = new JArray(ImageExtensions),
                    ["setup"] = available ? "Ready for local OCR." :
                        "Install Tesseract OCR and set its executable in Window > Wasim Development > Unity MCP Bridge, or add tesseract to PATH."
                };
            }

            string language = (string)args["language"] ?? "eng";
            int psm = args["pageSegmentation"] == null ? 6 : args["pageSegmentation"].Value<int>();
            int limit = ObjectAccess.Limit(args, "maximumCharacters", 4000, 100, 16000);
            ValidateOptions(language, psm);

            string imageFile = null;
            string tempFile = null;
            string displaySource = string.Empty;
            try
            {
                if (source == "asset" || source == "selected")
                {
                    string path = source == "selected" ?
                        AssetDatabase.GetAssetPath(Selection.activeObject) :
                        (string)args["path"];
                    imageFile = ResolveImageAsset(path);
                    displaySource = path;
                }
                else
                {
                    // Use the existing restricted Scene/Game capture pipeline:
                    // Game capture requires the focused Game View. Scene capture
                    // creates only a HideAndDontSave camera and cleans it up.
                    int width = ObjectAccess.Limit(args, "width", 1280, 64, 1280);
                    int height = ObjectAccess.Limit(args, "height", 720, 64, 1280);
                    JObject capture = ViewCapture.Capture(new JObject
                    {
                        ["view"] = source,
                        ["width"] = width,
                        ["height"] = height
                    });
                    JArray content = capture["_mcpContent"] as JArray;
                    JObject image = content == null ? null : content.OfType<JObject>()
                        .FirstOrDefault(x => (string)x["type"] == "image");
                    string base64 = (string)image?["data"];
                    if (string.IsNullOrEmpty(base64))
                        throw new InvalidOperationException("Unity view capture returned no image.");
                    byte[] bytes = Convert.FromBase64String(base64);
                    if (bytes.Length == 0 || bytes.LongLength > MaxImageBytes)
                        throw new InvalidOperationException("Captured image exceeds the safe OCR size limit.");
                    tempFile = Path.Combine(Path.GetTempPath(),
                        "mcp2unity-ocr-" + Guid.NewGuid().ToString("N") + ".jpg");
                    File.WriteAllBytes(tempFile, bytes);
                    imageFile = tempFile;
                    displaySource = source + " view (" + width + "x" + height + ")";
                }

                string stdout, stderr;
                int exitCode = Run(executable, Quote(imageFile) + " stdout -l " +
                    language + " --psm " + psm,
                    OcrTimeoutMilliseconds, out stdout, out stderr);
                if (exitCode != 0)
                    throw new InvalidOperationException(
                        "Local OCR failed (Tesseract exit " + exitCode + "). " +
                        ShortError(stderr) +
                        " Check the installed language pack (e.g. eng/msa/ind).");

                string recognized = (stdout ?? string.Empty).Trim();
                bool truncated = recognized.Length > limit;
                string shown = truncated ? recognized.Substring(0, limit) : recognized;
                return new JObject
                {
                    ["engine"] = "Tesseract (local only)",
                    ["source"] = source,
                    ["image"] = displaySource,
                    ["language"] = language,
                    ["pageSegmentation"] = psm,
                    ["text"] = shown,
                    ["hasText"] = recognized.Length > 0,
                    ["characterCount"] = recognized.Length,
                    ["truncated"] = truncated,
                    ["lines"] = new JArray(shown.Split(new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries).Take(100)),
                    ["note"] = "OCR text is approximate. Check visually before relying on labels, numbers or commands."
                };
            }
            finally
            {
                if (tempFile != null)
                {
                    try { if (File.Exists(tempFile)) File.Delete(tempFile); }
                    catch (IOException) { /* Temp cleanup on the next OS purge. */ }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        internal static void ValidateSource(string source)
        {
            if (source != "check" && source != "selected" && source != "asset" &&
                source != "scene" && source != "game")
                throw new ArgumentException("source must be check, selected, asset, scene or game.");
        }

        internal static void ValidateOptions(string language, int psm)
        {
            if (string.IsNullOrEmpty(language) || language.Length > 16 ||
                !Regex.IsMatch(language, @"^[a-z]{3}(\+[a-z]{3}){0,2}$",
                    RegexOptions.CultureInvariant))
                throw new ArgumentException(
                    "language must contain 1-3 three-letter Tesseract codes, e.g. eng, msa, eng+msa.");
            if (psm != 3 && psm != 6 && psm != 7 && psm != 11)
                throw new ArgumentException("pageSegmentation must be 3, 6, 7 or 11.");
        }

        internal static string ResolveImageAsset(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException(
                    "Select an image in Project or provide a project-local Assets/ image path.");
            string ext = Path.GetExtension(path);
            if (!ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Only PNG, JPEG, BMP and TIFF images are supported.");
            // Resolves traversal, linked paths and outside-project paths using
            // the existing security guard, without writing to the asset.
            string full = ProjectSecurity.ResolveAssetFile(path, true);
            if (!File.Exists(full))
                throw new FileNotFoundException("Image asset does not exist: " + path);
            long bytes = new FileInfo(full).Length;
            if (bytes == 0 || bytes > MaxImageBytes)
                throw new ArgumentException("Image file must be between 1 byte and 20 MB.");
            return full;
        }

        private static string ResolveExecutable()
        {
            string configured = BridgePreferences.TesseractExecutablePath;
            if (!string.Equals(configured, "tesseract", StringComparison.OrdinalIgnoreCase))
                return configured;
            string programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                string standard = Path.Combine(programFiles, "Tesseract-OCR", "tesseract.exe");
                if (File.Exists(standard)) return standard;
            }
            string x86 = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(x86))
            {
                string standard = Path.Combine(x86, "Tesseract-OCR", "tesseract.exe");
                if (File.Exists(standard)) return standard;
            }
            return "tesseract";
        }

        private static bool TryGetVersion(string executable, out string version)
        {
            try
            {
                string stdout, stderr;
                int exit = Run(executable, "--version", VersionTimeoutMilliseconds,
                    out stdout, out stderr);
                string value = (stdout + "\n" + stderr).Trim();
                version = value.Split(new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                return exit == 0 && version.StartsWith("tesseract", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is InvalidOperationException ||
                e is Win32Exception || e is System.IO.IOException)
            {
                version = string.Empty;
                return false;
            }
        }

        private static int Run(string executable, string arguments, int timeout,
            out string stdout, out string stderr)
        {
            stdout = string.Empty;
            stderr = string.Empty;
            var info = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = ProjectSecurity.ProjectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            try
            {
                using (Process process = Process.Start(info))
                {
                    if (process == null)
                        throw new InvalidOperationException("Unable to start local Tesseract OCR.");
                    // Drain both pipes concurrently so one cannot deadlock
                    // the other while Unity's main-thread request is blocked.
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(timeout))
                    {
                        try { process.Kill(); }
                        catch (InvalidOperationException) { }
                        throw new InvalidOperationException(
                            "Tesseract OCR exceeded the local 18-second limit. Use a smaller image.");
                    }
                    stdout = outputTask.GetAwaiter().GetResult();
                    stderr = errorTask.GetAwaiter().GetResult();
                    return process.ExitCode;
                }
            }
            catch (Win32Exception e)
            {
                throw new InvalidOperationException(
                    "Tesseract executable is unavailable. Install Tesseract OCR " +
                    "or configure its path in Window > Wasim Development > Unity MCP Bridge. " +
                    "OS detail: " + e.Message, e);
            }
        }

        private static string Quote(string value) =>
            "\"" + value.Replace("\"", "\\\"") + "\"";

        private static string ShortError(string value)
        {
            string text = (value ?? string.Empty).Trim();
            return text.Length > 1200 ? text.Substring(0, 1200) : text;
        }
    }
}
