using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DaraletPlayer.Map {
    /// <summary>One file in the world data release, as manifest.json lists it.</summary>
    [DataContract]
    public class WorldDataFile {
        [DataMember(Name = "name")] public string Name;
        /// <summary>Lower-case hex.</summary>
        [DataMember(Name = "sha256")] public string Sha256;
        [DataMember(Name = "size")] public long Size;
    }

    /// <summary>
    /// manifest.json, uploaded last whenever the world data release is published (.github/workflows/world-data.yml), so it
    /// only ever names files that are already there.
    /// </summary>
    [DataContract]
    public class WorldDataManifest {
        /// <summary>Raised only when old plugins couldn't read the files any more; they then keep what they have.</summary>
        [DataMember(Name = "format")] public int Format;
        /// <summary>When it was published, UTC, ISO 8601.</summary>
        [DataMember(Name = "published")] public string Published;
        [DataMember(Name = "commit")] public string Commit;
        [DataMember(Name = "files")] public List<WorldDataFile> Files = new List<WorldDataFile>();
    }

    /// <summary>
    /// Keeps the map image and locations.xml up to date from the "world-data" release on GitHub, so a new map or new places
    /// don't need a new plugin. A check reads manifest.json, and for each file this plugin wants whose hash differs from what
    /// it has, downloads it, checks the hash and the contents, and saves it in Folder. A file that's the same as the one built
    /// into the plugin isn't downloaded, and a downloaded one that's gone back to matching it is deleted.
    ///
    /// Checks run off the game's thread. Version goes up whenever a file changes, which is the window's cue (on the game's
    /// thread) to load it again. Daralet Admin has a copy of this file: keep the two the same.
    /// </summary>
    public class WorldData : IDisposable {
        /// <summary>The release both Daralet plugins read. Its tag stays "world-data"; each publish replaces its files.</summary>
        public const string DefaultBaseUrl = "https://github.com/daralet-ac/daralet-toolkit/releases/download/world-data/";
        public const string ManifestName = "manifest.json";
        public const int Format = 1;

        private readonly string folder;
        private readonly string[] wanted;
        private readonly Func<string, Task<byte[]>> download;
        private readonly Func<string, Stream> builtIn;
        private readonly Func<string, byte[], string> validate;
        private readonly Dictionary<string, string> builtInHashes = new Dictionary<string, string>();
        private readonly HttpClient client;

        private volatile bool busy;
        private volatile string status = "Not checked yet.";
        private int version;

        static WorldData() {
            // .NET Framework 4.8 doesn't always offer TLS 1.2 unless asked, and GitHub needs it
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        /// <param name="folder">Where downloaded files are kept</param>
        /// <param name="wanted">The files this plugin uses, by name in the release</param>
        /// <param name="builtIn">The copy of a file built into the plugin, or null</param>
        /// <param name="validate">Null if a downloaded file can be used, otherwise why not</param>
        /// <param name="download">Fetches a file from the release by name; null downloads from baseUrl</param>
        public WorldData(string folder, IEnumerable<string> wanted, Func<string, Stream> builtIn, Func<string, byte[], string> validate,
            Func<string, Task<byte[]>> download = null, string baseUrl = DefaultBaseUrl) {
            this.folder = folder;
            this.wanted = wanted.ToArray();
            this.builtIn = builtIn ?? (_ => null);
            this.validate = validate ?? ((_, _) => null);

            if (download == null) {
                client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(60) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Daralet/0.1");
                download = Download;
            }

            this.download = download;
        }

        public string Folder => folder;
        public bool Busy => busy;
        /// <summary>What the last check found, for the window.</summary>
        public string Status => status;
        /// <summary>Goes up by one each time a check changes a file.</summary>
        public int Version => Volatile.Read(ref version);

        /// <summary>The downloaded copy of a file, or null if there isn't one (the built-in one is to be used).</summary>
        public string PathFor(string name) {
            var path = Path.Combine(folder, name);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// The manifest of the files in Folder, or null if nothing's been checked yet
        /// </summary>
        public WorldDataManifest Current() {
            try {
                var path = PathFor(ManifestName);
                return path == null ? null : ReadManifest(File.ReadAllBytes(path));
            }
            catch {
                return null;
            }
        }

        /// <summary>Starts a check in the background, unless one is running.</summary>
        public void Start() {
            if (busy) {
                return;
            }

            busy = true;
            status = "Checking for updates...";

            Task.Run(async () => {
                try {
                    await Check().ConfigureAwait(false);
                }
                catch (Exception ex) {
                    status = $"Couldn't check for updates: {Message(ex)}";
                }
                finally {
                    busy = false;
                }
            });
        }

        /// <summary>
        /// Brings Folder up to date with the release. Returns the names of the files that changed. Status says what happened,
        /// including any file that couldn't be used (that one's left as it was, to try again next time).
        /// </summary>
        public async Task<List<string>> Check() {
            var changed = new List<string>();
            byte[] manifestBytes;

            try {
                manifestBytes = await download(ManifestName).ConfigureAwait(false);
            }
            catch (FileNotFoundException) {
                status = "No map updates have been published yet.";
                return changed;
            }

            var manifest = ReadManifest(manifestBytes);
            var problems = new List<string>();

            if (manifest.Format > Format) {
                status = "There's newer map data, but it needs a newer version of the plugin.";
                return changed;
            }

            Directory.CreateDirectory(folder);

            foreach (var name in wanted) {
                var entry = manifest.Files?.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

                if (entry == null || string.IsNullOrEmpty(entry.Sha256)) {
                    continue;
                }

                try {
                    if (await Update(name, entry.Sha256.ToLowerInvariant()).ConfigureAwait(false)) {
                        changed.Add(name);
                    }
                }
                catch (Exception ex) {
                    problems.Add($"{name}: {Message(ex)}");
                }
            }

            if (problems.Count == 0) {
                WriteFile(ManifestName, Encoding.UTF8.GetBytes(WriteManifest(manifest)));
            }

            if (changed.Count > 0) {
                Interlocked.Increment(ref version);
            }

            var published = DescribePublished(manifest);

            if (problems.Count > 0) {
                status = $"Some map data couldn't be updated ({string.Join("; ", problems)}). It'll be tried again next time.";
            }
            else if (changed.Count > 0) {
                status = $"Updated {string.Join(" and ", changed)} to the version published {published}.";
            }
            else {
                status = $"Up to date (published {published}).";
            }

            return changed;
        }

        /// <summary>
        /// Makes one file match the release's hash. True if what the plugin should use changed.
        /// </summary>
        private async Task<bool> Update(string name, string sha256) {
            var path = Path.Combine(folder, name);

            if (File.Exists(path) && Hash(File.ReadAllBytes(path)) == sha256) {
                return false;
            }

            if (BuiltInHash(name) == sha256) {
                // the plugin already has this one built in
                if (!File.Exists(path)) {
                    return false;
                }

                File.Delete(path);
                return true;
            }

            var bytes = await download(name).ConfigureAwait(false);

            if (Hash(bytes) != sha256) {
                throw new InvalidDataException("the download didn't match the manifest (it may be being published right now)");
            }

            var problem = validate(name, bytes);

            if (problem != null) {
                throw new InvalidDataException(problem);
            }

            WriteFile(name, bytes);
            return true;
        }

        private string BuiltInHash(string name) {
            if (builtInHashes.TryGetValue(name, out var hash)) {
                return hash;
            }

            using (var stream = builtIn(name)) {
                if (stream == null) {
                    hash = null;
                }
                else {
                    using (var sha = SHA256.Create()) {
                        hash = Hex(sha.ComputeHash(stream));
                    }
                }
            }

            builtInHashes[name] = hash;
            return hash;
        }

        /// <summary>
        /// Written to a temporary file first, so the game never reads half a file and a crash can't leave one
        /// </summary>
        private void WriteFile(string name, byte[] bytes) {
            var path = Path.Combine(folder, name);
            var temporary = path + ".tmp";

            File.WriteAllBytes(temporary, bytes);

            if (File.Exists(path)) {
                File.Replace(temporary, path, null);
            }
            else {
                File.Move(temporary, path);
            }
        }

        private async Task<byte[]> Download(string name) {
            using (var response = await client.GetAsync(Uri.EscapeDataString(name)).ConfigureAwait(false)) {
                if (response.StatusCode == HttpStatusCode.NotFound) {
                    throw new FileNotFoundException($"{name} hasn't been published");
                }

                if (!response.IsSuccessStatusCode) {
                    throw new HttpRequestException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase} for {name}");
                }

                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        public static string Hash(byte[] bytes) {
            using (var sha = SHA256.Create()) {
                return Hex(sha.ComputeHash(bytes));
            }
        }

        private static string Hex(byte[] hash) {
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }

        public static WorldDataManifest ReadManifest(byte[] bytes) {
            using (var stream = new MemoryStream(bytes)) {
                return (WorldDataManifest)new DataContractJsonSerializer(typeof(WorldDataManifest)).ReadObject(stream)
                    ?? throw new InvalidDataException("manifest.json is empty");
            }
        }

        public static string WriteManifest(WorldDataManifest manifest) {
            using (var stream = new MemoryStream()) {
                new DataContractJsonSerializer(typeof(WorldDataManifest)).WriteObject(stream, manifest);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>"2026-10-20 14:05 UTC", or "at an unknown time".</summary>
        public static string DescribePublished(WorldDataManifest manifest) {
            return DateTime.TryParse(manifest?.Published, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var at)
                ? at.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture)
                : "at an unknown time";
        }

        /// <summary>
        /// Null if the bytes are a square PNG, otherwise what's wrong. Read from the PNG header, so nothing is decoded.
        /// </summary>
        public static string CheckSquarePng(byte[] bytes) {
            var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

            if (bytes == null || bytes.Length < 24 || !signature.SequenceEqual(bytes.Take(8))) {
                return "it isn't a PNG";
            }

            int BigEndian(int at) => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];

            var width = BigEndian(16);
            var height = BigEndian(20);

            return width == height && width > 0 ? null : $"it's {width}x{height}, not square";
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
