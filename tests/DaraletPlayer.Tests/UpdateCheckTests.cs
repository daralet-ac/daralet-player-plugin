using System;
using System.Threading.Tasks;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class UpdateCheckTests {
        /// <summary>What GitHub's releases/latest sends, cut down to what's read and a little more.</summary>
        private static string Release(string tag) {
            return "{\"tag_name\":\"" + tag + "\",\"name\":\"Daralet\",\"html_url\":\"https://github.com/daralet-ac/daralet-player-plugin/releases/tag/" + tag + "\",\"prerelease\":false,\"assets\":[]}";
        }

        private static UpdateCheck Check(string current, string json) {
            return new UpdateCheck(new Version(current), () => Task.FromResult(json));
        }

        [TestMethod]
        public void ParseTagReadsTheReleaseWorkflowsTags() {
            Assert.AreEqual(new Version(0, 5, 0, 0), UpdateCheck.ParseTag("v0.5.0"));
            Assert.AreEqual(new Version(1, 2, 0, 0), UpdateCheck.ParseTag("1.2"));
            Assert.AreEqual(new Version(0, 6, 0, 0), UpdateCheck.ParseTag("v0.6.0-beta"));
            Assert.IsNull(UpdateCheck.ParseTag("world-data"));
            Assert.IsNull(UpdateCheck.ParseTag(null));
        }

        [TestMethod]
        public void TheSameVersionIsNotNewer() {
            // the assembly says 0.5.0.0 and the tag 0.5.0, which Version alone puts in the wrong order
            Assert.IsTrue(new Version(0, 5, 0) < new Version(0, 5, 0, 0));

            var check = Check("0.5.0.0", Release("v0.5.0"));
            check.Check().Wait();

            Assert.IsNull(check.Newer);
            StringAssert.Contains(check.Status, "Up to date");
        }

        [TestMethod]
        public void ANewerReleaseIsOffered() {
            var check = Check("0.5.0.0", Release("v0.6.0"));
            check.Check().Wait();

            Assert.IsNotNull(check.Newer);
            Assert.AreEqual(new Version(0, 6, 0, 0), check.Newer.Version);
            Assert.AreEqual("https://github.com/daralet-ac/daralet-player-plugin/releases/tag/v0.6.0", check.Newer.Url);
        }

        [TestMethod]
        public void AnOlderReleaseIsNotOffered() {
            // a build newer than the latest release, as a developer's is
            var check = Check("0.6.0.0", Release("v0.5.0"));
            check.Check().Wait();

            Assert.IsNull(check.Newer);
        }

        [TestMethod]
        public void NoReleaseOrNoVersionOffersNothing() {
            var none = Check("0.5.0.0", null);
            none.Check().Wait();
            Assert.IsNull(none.Newer);

            var odd = Check("0.5.0.0", Release("latest"));
            odd.Check().Wait();
            Assert.IsNull(odd.Newer);
            StringAssert.Contains(odd.Status, "isn't a version");
        }
    }
}
