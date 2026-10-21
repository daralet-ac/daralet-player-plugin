using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace DaraletPlayer.Feed {
    /// <summary>The fields of GitHub's release JSON the update check reads.</summary>
    [DataContract]
    public class GitHubRelease {
        [DataMember(Name = "tag_name")] public string TagName;
        [DataMember(Name = "html_url")] public string HtmlUrl;
    }

    /// <summary>
    /// Asks GitHub, once when the plugin starts and off the game's thread, whether a newer release than this build is out.
    /// The plugin only says so (a line in chat, a button on the window's top line); the player runs the new installer.
    /// "Latest" on GitHub never means a draft or a pre-release, so the world-data release is never it.
    /// </summary>
    public class UpdateCheck : IDisposable {
        public const string LatestApiUrl = "https://api.github.com/repos/daralet-ac/daralet-player-plugin/releases/latest";
        public const string LatestPageUrl = "https://github.com/daralet-ac/daralet-player-plugin/releases/latest";

        private readonly Func<Task<string>> fetch;
        private readonly HttpClient client;

        private volatile string status = "Not checked yet.";
        private volatile Available available;

        /// <summary>A release newer than this build: its version and its page.</summary>
        public class Available {
            public Version Version;
            public string Url;
        }

        static UpdateCheck() {
            // .NET Framework 4.8 doesn't always offer TLS 1.2 unless asked, and GitHub needs it
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        /// <param name="current">This build's version</param>
        /// <param name="fetch">Gets the latest release's JSON, or null if there's no release; null asks GitHub</param>
        public UpdateCheck(Version current, Func<Task<string>> fetch = null) {
            Current = Normalize(current);

            if (fetch == null) {
                client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                // GitHub's API refuses requests without a User-Agent
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"Daralet/{Current.ToString(3)}");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                fetch = Fetch;
            }

            this.fetch = fetch;
        }

        public Version Current { get; }

        /// <summary>A newer release, once a check has found one; null otherwise.</summary>
        public Available Newer => available;

        /// <summary>What the last check found, for a tooltip.</summary>
        public string Status => status;

        /// <summary>Starts the check in the background.</summary>
        public void Start() {
            status = "Checking for a new version...";

            Task.Run(async () => {
                try {
                    await Check().ConfigureAwait(false);
                }
                catch (Exception ex) {
                    status = $"Couldn't check for a new version: {Message(ex)}";
                }
            });
        }

        public async Task Check() {
            var json = await fetch().ConfigureAwait(false);

            if (json == null) {
                status = "No release has been published yet.";
                return;
            }

            var release = ReadRelease(json);
            var latest = ParseTag(release?.TagName);

            if (latest == null) {
                status = $"The latest release's tag ({release?.TagName ?? "none"}) isn't a version.";
                return;
            }

            if (latest > Current) {
                available = new Available { Version = latest, Url = IsReleasePage(release.HtmlUrl) ? release.HtmlUrl : LatestPageUrl };
                status = $"Daralet {latest.ToString(3)} is out (you have {Current.ToString(3)}).";
            }
            else {
                status = $"Up to date ({Current.ToString(3)} is the latest).";
            }
        }

        /// <summary>Whether a link is one of this plugin's release pages, the only links its chat links may open.</summary>
        public static bool IsReleasePage(string url) {
            return url != null && url.StartsWith("https://github.com/daralet-ac/daralet-player-plugin/releases/", StringComparison.Ordinal)
                && url.IndexOfAny(new[] { ' ', '"', '\'', '<', '>' }) < 0;
        }

        /// <summary>"v0.5.0" or "0.5.0" (anything after a "-" ignored) as a four-part version, or null.</summary>
        public static Version ParseTag(string tag) {
            if (string.IsNullOrWhiteSpace(tag)) {
                return null;
            }

            var text = tag.Trim().TrimStart('v', 'V');
            var dash = text.IndexOf('-');

            if (dash >= 0) {
                text = text.Substring(0, dash);
            }

            return Version.TryParse(text, out var version) ? Normalize(version) : null;
        }

        /// <summary>
        /// Every part present: Version counts a missing part as -1, so 0.5.0 would otherwise come before 0.5.0.0 (what the
        /// assembly says it is).
        /// </summary>
        public static Version Normalize(Version version) {
            if (version == null) {
                return new Version(0, 0, 0, 0);
            }

            return new Version(Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        public static GitHubRelease ReadRelease(string json) {
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))) {
                return (GitHubRelease)new DataContractJsonSerializer(typeof(GitHubRelease)).ReadObject(stream);
            }
        }

        private async Task<string> Fetch() {
            using (var response = await client.GetAsync(LatestApiUrl).ConfigureAwait(false)) {
                if (response.StatusCode == HttpStatusCode.NotFound) {
                    return null;
                }

                if (!response.IsSuccessStatusCode) {
                    // 403 is usually GitHub's limit of 60 requests an hour from one address
                    throw new HttpRequestException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private static string Message(Exception ex) {
            while (ex is AggregateException aggregate && aggregate.InnerException != null) {
                ex = aggregate.InnerException;
            }

            if (ex is TaskCanceledException) {
                return "GitHub took too long to answer";
            }

            return ex is HttpRequestException && ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }

        public void Dispose() {
            client?.Dispose();
        }
    }
}
