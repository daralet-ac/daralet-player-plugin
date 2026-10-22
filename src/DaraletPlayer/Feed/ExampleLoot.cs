using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The Example Loot Profile: a starting point to copy and change, in folders by kind of item, marked by how well each
    /// rolled. It's Resources/example-loot-profile.json, a profile as the Loot tab's To file saves one, so changing it is
    /// saving a profile and copying the file over. New settings start with it after "My loot"; the Loot tab's Example
    /// button adds it again.
    /// </summary>
    public static class ExampleLoot {
        public const string Name = "Example Loot Profile";

        private const string ResourceName = "DaraletPlayer.Resources.example-loot-profile.json";

        public static LootList Make() {
            LootList list = null;

            try {
                using (var stream = typeof(ExampleLoot).Assembly.GetManifestResourceStream(ResourceName)) {
                    if (stream != null) {
                        list = Json.Read<LootList>(stream);
                    }
                }
            }
            catch (Exception) {
                // a file that won't read leaves an empty example rather than no settings at all
            }

            list = list ?? new LootList { Rules = new List<LootRule>() };
            list.Name = Name;
            return list;
        }
    }
}
