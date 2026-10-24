using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The Example Loot Profile: a starting point to copy and change, in folders by kind of item, marked by how well each
    /// rolled. It's Resources/example-loot-profile.json, a profile as the Loot tab's To file saves one, so changing it is
    /// saving a profile and copying the file over. The colorless one (example-loot-profile-colorless.json) is the same
    /// rules marked white, 50% gray and black instead of gold, silver and bronze. New settings start with both after
    /// "My loot"; the Loot tab's Example buttons add them again.
    /// </summary>
    public static class ExampleLoot {
        public const string Name = "Example Loot Profile";
        public const string ColorlessName = "Example Loot Profile (colorless)";

        private const string ResourceName = "DaraletPlayer.Resources.example-loot-profile.json";
        private const string ColorlessResourceName = "DaraletPlayer.Resources.example-loot-profile-colorless.json";

        public static LootList Make() {
            return Read(ResourceName, Name);
        }

        public static LootList MakeColorless() {
            return Read(ColorlessResourceName, ColorlessName);
        }

        private static LootList Read(string resource, string name) {
            LootList list = null;

            try {
                using (var stream = typeof(ExampleLoot).Assembly.GetManifestResourceStream(resource)) {
                    if (stream != null) {
                        list = Json.Read<LootList>(stream);
                    }
                }
            }
            catch (Exception) {
                // a file that won't read leaves an empty example rather than no settings at all
            }

            list = list ?? new LootList { Rules = new List<LootRule>() };
            list.Name = name;
            return list;
        }
    }
}
