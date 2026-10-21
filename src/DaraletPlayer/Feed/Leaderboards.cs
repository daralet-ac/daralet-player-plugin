using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>The boards, in the website's order.</summary>
    public static class Leaderboards {
        /// <summary>How many rows each board asks for, as the website does.</summary>
        public const int Limit = 50;

        public static readonly (string Value, string Label)[] CapstoneDungeons = {
            ("EmpyreanGarrison", "Empyrean Garrison"),
            ("FolthidCellar", "Folthid Cellar"),
            ("GlendenWoodDungeon", "Glenden Wood Dungeon"),
            ("GredalineConsulate", "Gredaline Consulate"),
            ("GreenMireGrave", "Green Mire Grave"),
            ("GrievousVault", "Grievous Vault"),
            ("HallsOfTheHelm", "Halls of the Helm"),
            ("LugianMines", "Lugian Mines"),
            ("MageAcademy", "Mage Academy"),
            ("ManseOfPanderlou", "Manse of Panderlou"),
            ("MinesOfColier", "Mines of Colier"),
            ("MinesOfDespair", "Mines of Despair"),
            ("MountainFortress", "Mountain Fortress"),
            ("SandShallow", "Sand Shallow"),
            ("SmugglersHideaway", "Smugglers Hideaway"),
        };

        /// <summary>Fellowship sizes the capstone board splits by: solo, two, and three or more.</summary>
        public static readonly (int Value, string Label)[] CapstoneSizes = { (1, "Solo"), (2, "2"), (3, "3+") };

        /// <summary>The arena brackets the board has.</summary>
        public static readonly (int Value, string Label)[] ArenaSizes = { (1, "1v1"), (2, "2v2"), (3, "3v3") };

        public static readonly LeaderboardDefinition[] All = {
            Board("level", "Level", "The highest-level characters, by total experience.",
                _ => $"api/leaderboards/level?limit={Limit}",
                ("Character", r => r.CharacterName),
                ("Level", r => N(r.Level)),
                ("Total XP", r => N(r.TotalXp))),

            Board("arena", "Arena", "The best arena ratings in a bracket.",
                o => $"api/leaderboards/arena?size={o.ArenaSize}&scaled={(o.ArenaScaled ? "true" : "false")}&limit={Limit}",
                ("Character", r => r.CharacterName),
                ("Rating", r => N(r.Rating)),
                ("W / L / D", r => $"{r.Wins ?? 0} / {r.Losses ?? 0} / {r.Draws ?? 0}")),

            Board("capstone", "Capstones", "The fastest capstone runs, by dungeon, fellowship size and difficulty.",
                CapstonePath,
                ("Fellowship", r => string.Join(", ", r.CharacterNames ?? new List<string>())),
                ("Time", r => r.CompletionSeconds.HasValue ? Duration(r.CompletionSeconds.Value) : ""),
                ("Date", r => Date(r.CompletedAt))),

            Board("deaths", "Deaths", "Who has died the most.",
                _ => $"api/leaderboards/deaths?limit={Limit}",
                ("Character", r => r.CharacterName),
                ("Deaths", r => N(r.NumDeaths))),

            Board("allegiance", "Allegiance rank", "The highest allegiance ranks.",
                _ => $"api/leaderboards/allegiance-rank?limit={Limit}",
                ("Character", r => r.CharacterName),
                ("Allegiance rank", r => N(r.AllegianceRank))),

            Board("creatures", "Dangerous creatures", "The creatures that have killed the most players.",
                _ => $"api/leaderboards/dangerous-creatures?limit={Limit}",
                ("Creature", r => r.CreatureName),
                ("Player deaths caused", r => N(r.PlayerDeathsCaused))),

            Board("towns", "Town attunements", "Who has attuned to the most towns.",
                _ => $"api/leaderboards/town-attunements?limit={Limit}",
                ("Character", r => r.CharacterName),
                ("Towns attuned", r => N(r.TownsAttuned))),

            Board("trade", "Trade skills", "The best at each trade skill.",
                _ => "api/leaderboards/trade-skills",
                ("Skill", r => r.SkillName),
                ("Character", r => r.CharacterName ?? "-"),
                ("Skill level", r => N(r.SkillLevel))),
        };

        /// <summary>The arena board, whose top the Arena tab shows too.</summary>
        public static LeaderboardDefinition Arena => All.First(board => board.Id == "arena");

        /// <summary>The capstone difficulties: None (no Dungeon: Difficulty mod), then I to XX.</summary>
        public static (int Value, string Label)[] CapstoneDifficulties() {
            string[] numerals = {
                "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X",
                "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX",
            };

            return new[] { (0, "None") }.Concat(numerals.Select((n, i) => (i + 1, n))).ToArray();
        }

        /// <summary>754 -> "12:34", 4000 -> "1:06:40", as the website writes run times.</summary>
        public static string Duration(double totalSeconds) {
            var s = (int)Math.Max(0, Math.Round(totalSeconds));
            var h = s / 3600;
            var m = s % 3600 / 60;
            var sec = s % 60;
            return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
        }

        /// <summary>An ISO date as yyyy-MM-dd, or "" if it can't be read.</summary>
        public static string Date(string iso) {
            var styles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, styles, out var date)) {
                return "";
            }

            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string CapstonePath(LeaderboardOptions o) {
            var dungeon = Uri.EscapeDataString(o.CapstoneDungeon ?? "");
            return $"api/leaderboards/capstone?dungeon={dungeon}&fellowshipSize={o.CapstoneSize}"
                + $"&difficulty={o.CapstoneDifficulty}&limit={Limit}";
        }

        private static LeaderboardDefinition Board(string id, string label, string tip, Func<LeaderboardOptions, string> path,
            params (string Header, Func<LeaderboardRow, string> Cell)[] columns) {
            return new LeaderboardDefinition { Id = id, Label = label, Tip = tip, Path = path, Columns = columns };
        }

        private static string N(long? value) {
            return value?.ToString("N0", CultureInfo.InvariantCulture) ?? "";
        }
    }
}
