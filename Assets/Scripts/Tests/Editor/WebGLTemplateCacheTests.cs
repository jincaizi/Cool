using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class WebGLTemplateCacheTests
    {
        private static string TemplatePath =>
            Path.Combine(Application.dataPath, "WebGLTemplates/MobileLandscape/index.html");

        [Test]
        public void test_template_has_build_stamp_marker()
        {
            string html = File.ReadAllText(TemplatePath);
            StringAssert.Contains("__BUILD_STAMP__", html,
                "WebGLBuildScript 需要在产物里替换这个占位符");
        }

        [Test]
        public void test_template_builds_cache_key_from_version_and_stamp()
        {
            string html = File.ReadAllText(TemplatePath);
            StringAssert.Contains("var cacheKey = buildVersion + \"-\" + buildStamp;", html);
            StringAssert.Contains("var cacheBust = \"?v=\" + encodeURIComponent(cacheKey);", html);
            StringAssert.Contains("if (buildStamp.charAt(0) === \"_\") buildStamp = String(Date.now());", html);
            StringAssert.Contains("productVersion: cacheKey", html);
        }

        [Test]
        public void test_template_versioned_urls_cover_all_payloads()
        {
            string html = File.ReadAllText(TemplatePath);
            StringAssert.Contains("var dataUrl = buildUrl + \"/{{{ DATA_FILENAME }}}\" + cacheBust;", html);
            StringAssert.Contains("frameworkUrl: buildUrl + \"/{{{ FRAMEWORK_FILENAME }}}\" + cacheBust", html);
            StringAssert.Contains("loaderUrl = buildUrl + \"/{{{ LOADER_FILENAME }}}\" + cacheBust", html);
            StringAssert.Contains("codeUrl: buildUrl + \"/{{{ CODE_FILENAME }}}\" + cacheBust", html);
        }

        [Test]
        public void test_template_data_url_is_immutable_and_bundles_keep_default()
        {
            string html = File.ReadAllText(TemplatePath);
            StringAssert.Contains("if (url === dataUrl) return \"immutable\";", html);
            StringAssert.Contains("url.match(/\\.bundle/) ? \"must-revalidate\" : \"no-store\"", html);
        }
    }
}
