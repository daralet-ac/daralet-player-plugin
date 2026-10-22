using System;
using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Folders in a rule list, and moving rules about. A folder is only a name on its rules (LootRule.Folder), and its
    /// rules are always kept together, where its first one is, so the list's order is still the order rules are checked
    /// in, folders and all.
    /// </summary>
    public static class LootFolders {
        /// <summary>Puts each folder's rules together, where its first one is, keeping their order.</summary>
        public static void Gather(List<LootRule> rules) {
            var gathered = new List<LootRule>(rules.Count);
            var seen = new HashSet<string>();

            foreach (var rule in rules) {
                if (string.IsNullOrEmpty(rule.Folder)) {
                    gathered.Add(rule);
                }
                else if (seen.Add(rule.Folder)) {
                    gathered.AddRange(rules.Where(r => r.Folder == rule.Folder));
                }
            }

            rules.Clear();
            rules.AddRange(gathered);
        }

        /// <summary>
        /// Moves rules, in the order the list has them, to just before another (null for the end), into that folder ("" for
        /// none).
        /// </summary>
        public static void Move(List<LootRule> rules, IEnumerable<LootRule> moving, LootRule before, string folder) {
            var moved = rules.Where(moving.Contains).ToList();

            if (moved.Count == 0) {
                return;
            }

            // before one of the moving rules is before the first one after it that stays (the rule just below a folder,
            // dropped on that folder, goes in at its end)
            if (before != null && moved.Contains(before)) {
                before = rules.Skip(rules.IndexOf(before)).FirstOrDefault(r => !moved.Contains(r));
            }

            rules.RemoveAll(moved.Contains);
            var at = before == null ? rules.Count : rules.IndexOf(before);
            rules.InsertRange(at < 0 ? rules.Count : at, moved);

            foreach (var rule in moved) {
                rule.Folder = folder ?? "";
            }

            Gather(rules);
        }

        /// <summary>Moves a whole folder to just before a rule (null for the end), before that rule's folder if it's in one.</summary>
        public static void MoveFolder(List<LootRule> rules, string folder, LootRule before) {
            if (before != null && before.Folder == folder) {
                return;
            }

            if (before != null && !string.IsNullOrEmpty(before.Folder)) {
                before = rules.First(r => r.Folder == before.Folder);
            }

            Move(rules, rules.Where(r => r.Folder == folder).ToList(), before, folder);
        }

        /// <summary>Puts rules in a folder, which goes where the first of them is.</summary>
        public static void Group(List<LootRule> rules, IEnumerable<LootRule> chosen, string folder) {
            var first = rules.FirstOrDefault(chosen.Contains);

            if (first == null) {
                return;
            }

            // the rule after the first chosen one that isn't moving marks the place
            var moving = rules.Where(chosen.Contains).ToList();
            var at = rules.IndexOf(first);
            var before = rules.Skip(at).FirstOrDefault(r => !moving.Contains(r));

            // into the middle of another folder would split it: before that folder instead
            if (before != null && !string.IsNullOrEmpty(before.Folder) && at > 0 && rules[at - 1].Folder == before.Folder) {
                before = rules.First(r => r.Folder == before.Folder);
            }

            Move(rules, moving, before, folder);
        }

        /// <summary>Copies rules, each just below the one it copies, in the same folder.</summary>
        public static void Duplicate(List<LootRule> rules, IEnumerable<LootRule> these) {
            foreach (var rule in rules.Where(these.Contains).ToList()) {
                rules.Insert(rules.IndexOf(rule) + 1, rule.Copy());
            }
        }

        /// <summary>Copies a folder and every rule in it, just below it, as "Name copy" (or "Name copy 2"...), open or shut alike.</summary>
        public static void DuplicateFolder(LootList list, string folder) {
            var rules = list.Rules;
            var copies = rules.Where(r => r.Folder == folder).Select(r => r.Copy()).ToList();

            var name = UniqueName(list, folder + " copy");

            if (copies.Count == 0) {
                if (list.EmptyFolders.Contains(folder)) {
                    list.EmptyFolders.Insert(list.EmptyFolders.IndexOf(folder) + 1, name);
                }

                return;
            }

            foreach (var copy in copies) {
                copy.Folder = name;
            }

            rules.InsertRange(rules.FindLastIndex(r => r.Folder == folder) + 1, copies);

            if (list.ClosedFolders.Contains(folder)) {
                list.ClosedFolders.Add(name);
            }
        }

        /// <summary>Takes every rule out of a folder, leaving them where they are.</summary>
        public static void Ungroup(LootList list, string folder) {
            foreach (var rule in list.Rules.Where(r => r.Folder == folder)) {
                rule.Folder = "";
            }

            list.ClosedFolders.Remove(folder);
            list.EmptyFolders.Remove(folder);
        }

        /// <summary>Renames a folder; to a name another folder has, it joins that one.</summary>
        public static void Rename(LootList list, string from, string to) {
            to = (to ?? "").Trim();

            if (to.Length == 0 || to == from) {
                return;
            }

            var taken = list.EmptyFolders.Contains(to) || list.Rules.Any(r => r.Folder == to);
            var empty = list.EmptyFolders.IndexOf(from);

            if (empty >= 0 && taken) {
                list.EmptyFolders.RemoveAt(empty);
            }
            else if (empty >= 0) {
                list.EmptyFolders[empty] = to;
            }

            foreach (var rule in list.Rules.Where(r => r.Folder == from)) {
                rule.Folder = to;
            }

            if (list.ClosedFolders.Remove(from) && !list.ClosedFolders.Contains(to)) {
                list.ClosedFolders.Add(to);
            }

            Gather(list.Rules);
            Tidy(list);
        }

        /// <summary>Adds an empty folder at the top of the list, and says what it's called.</summary>
        public static string AddEmpty(LootList list) {
            var name = UniqueName(list, "New folder");
            list.EmptyFolders.Insert(0, name);
            return name;
        }

        /// <summary>Drops the empty folders that have rules now, and the shut ones that are gone.</summary>
        public static void Tidy(LootList list) {
            list.EmptyFolders.RemoveAll(f => list.Rules.Any(r => r.Folder == f));
            list.ClosedFolders.RemoveAll(f => !list.EmptyFolders.Contains(f) && !list.Rules.Any(r => r.Folder == f));
        }

        /// <summary>A folder name the list doesn't have yet: the given one, or it with " 2", " 3"... after it.</summary>
        public static string UniqueName(LootList list, string name) {
            var baseName = string.IsNullOrWhiteSpace(name) ? "New folder" : name.Trim();
            var candidate = baseName;
            bool Taken(string folder) => string.Equals(folder, candidate, StringComparison.OrdinalIgnoreCase);

            for (var i = 2; list.Rules.Any(r => Taken(r.Folder)) || list.EmptyFolders.Any(Taken); i++) {
                candidate = $"{baseName} {i}";
            }

            return candidate;
        }
    }
}
