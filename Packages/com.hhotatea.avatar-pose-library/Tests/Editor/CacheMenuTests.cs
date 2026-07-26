using com.hhotatea.avatar_pose_library.editor;
using NUnit.Framework;

namespace com.hhotatea.avatar_pose_library.tests
{
    public class CacheMenuTests
    {
        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase(@"Assets\~APLCache\", "Assets/~APLCache")]
        [TestCase("Assets/~APLCache///", "Assets/~APLCache")]
        public void NormalizeCachePath_NormalizesSeparatorsAndTrailingSlashes(
            string path,
            string expected)
        {
            Assert.That(CacheMenu.NormalizeCachePath(path), Is.EqualTo(expected));
        }

        [TestCase("Assets/~APLCache", true)]
        [TestCase("Assets/Custom/APLCache", true)]
        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("Assets", false)]
        [TestCase("Assets/", false)]
        [TestCase("Packages/APLCache", false)]
        [TestCase("Assets/../Other", false)]
        [TestCase("Assets/./APLCache", false)]
        public void IsSafeCachePath_OnlyAllowsNestedAssetsFolders(string path, bool expected)
        {
            var normalizedPath = CacheMenu.NormalizeCachePath(path);

            Assert.That(CacheMenu.IsSafeCachePath(normalizedPath), Is.EqualTo(expected));
        }

        [TestCase(-1, "Unknown")]
        [TestCase(0, "0 B")]
        [TestCase(1023, "1023 B")]
        [TestCase(1024, "1 KB")]
        [TestCase(1048576, "1 MB")]
        [TestCase(1073741824, "1 GB")]
        public void FormatBytes_UsesReadableBinaryUnits(long bytes, string expected)
        {
            Assert.That(CacheMenu.FormatBytes(bytes), Is.EqualTo(expected));
        }
    }
}