using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WasimDevelopment.UnityMcpBridge.Tests
{
    public sealed partial class BridgeIntegrationTests
    {
        [Test]
        public void ImageOcrCatalogIsReadOnlyAndStrict()
        {
            JObject tool = McpToolCatalog.Build().OfType<JObject>()
                .Single(t => (string)t["name"] == "unity_read_image_text");
            Assert.IsTrue((bool)tool["annotations"]["readOnlyHint"]);
            Assert.IsFalse((bool)tool["annotations"]["destructiveHint"]);
            Assert.IsFalse((bool)tool["inputSchema"]["additionalProperties"]);
            JArray modes = (JArray)tool["inputSchema"]["properties"]["source"]["enum"];
            CollectionAssert.AreEquivalent(
                new[] { "check", "selected", "asset", "scene", "game" },
                modes.Values<string>().ToArray());
        }

        [Test]
        public void ImageOcrCheckReturnsAvailabilityWithoutRequiringAnEngine()
        {
            JObject check = (JObject)UnityToolExecutor.Execute("unity_read_image_text",
                new JObject { ["source"] = "check" });
            Assert.AreEqual("Tesseract (local only)", (string)check["engine"]);
            Assert.AreEqual(JTokenType.Boolean, check["available"].Type);
            Assert.IsNotEmpty((string)check["setup"]);
        }

        [Test]
        public void ImageOcrBundleResolutionIsReadOnlyAndChecksRequiredFiles()
        {
            string bundled = ImageTextReader.GetBundledOcrDirectory();
            if (bundled == null)
                Assert.Pass("Native OCR binary not yet vendored or not on Windows-x64.");
            Assert.IsTrue(File.Exists(Path.Combine(bundled, "tesseract.exe")));
            Assert.IsTrue(File.Exists(Path.Combine(bundled, "tessdata", "eng.traineddata")));
            JObject check = (JObject)UnityToolExecutor.Execute("unity_read_image_text",
                new JObject { ["source"] = "check" });
            Assert.AreEqual("bundled-windows-x64", (string)check["provider"]);
            Assert.IsTrue((bool)check["bundledAvailable"]);
        }

        [Test]
        public void ImageOcrRejectsUnsupportedSourceAndInvalidOptions()
        {
            Assert.Throws<ArgumentException>(() => ImageTextReader.Read(
                new JObject { ["source"] = "http" }));
            Assert.Throws<ArgumentException>(() =>
                ImageTextReader.ValidateOptions("eng;rm -rf", 6));
            Assert.Throws<ArgumentException>(() =>
                ImageTextReader.ValidateOptions("eng", 8));
            Assert.DoesNotThrow(() =>
                ImageTextReader.ValidateOptions("eng+msa", 11));
        }

        [Test]
        public void ImageOcrNeverReadsOutsideAssetsOrNonImageFiles()
        {
            Assert.Throws<ArgumentException>(() =>
                ImageTextReader.ResolveImageAsset("C:/Windows/system.ini"));
            Assert.Throws<ArgumentException>(() =>
                ImageTextReader.ResolveImageAsset("../Secrets/passwords.png"));
            Assert.Throws<ArgumentException>(() =>
                ImageTextReader.ResolveImageAsset("Assets/SomeScript.cs"));
            Assert.Throws<ArgumentException>(() =>
                ImageTextReader.ResolveImageAsset("Packages/a/screenshot.png"));
            Assert.Throws<ArgumentException>(() =>
                ImageTextReader.ResolveImageAsset("Assets/../Outside.png"));
        }

        [Test]
        public void ImageOcrSelectedGameObjectIsNotTreatedAsImage()
        {
            UnityEngine.Object previous = Selection.activeObject;
            try
            {
                Selection.activeObject = _root;
                Assert.Throws<ArgumentException>(() =>
                    ImageTextReader.Read(new JObject
                    {
                        ["source"] = "selected",
                        ["language"] = "eng"
                    }));
            }
            finally
            {
                Selection.activeObject = previous;
            }
        }
    }
}
