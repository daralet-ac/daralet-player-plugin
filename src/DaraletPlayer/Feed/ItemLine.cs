namespace DaraletPlayer.Feed {
    /// <summary>How a line of an item tooltip is drawn.</summary>
    public enum ItemLineStyle {
        /// <summary>The item's name.</summary>
        Title,
        /// <summary>What kind of item it is, and other quiet facts.</summary>
        Detail,
        /// <summary>A section's name: Weapon, Armor, Spells...</summary>
        Heading,
        Text,
        /// <summary>Something good about it (a bonus).</summary>
        Good,
        /// <summary>Something that holds it back (a penalty).</summary>
        Bad,
        /// <summary>Spells, coloured by cantrip level: Minor, Major, Epic, Legendary.</summary>
        MinorSpell,
        MajorSpell,
        EpicSpell,
        LegendarySpell,
    }

    public class ItemLine {
        public string Text;
        public ItemLineStyle Style;

        /// <summary>The stat this line shows, by its name in appraisal's Roll Quality section, if it's one that rolls.</summary>
        public string RollStat;

        /// <summary>Where the stat rolled in its range, 0 to 1, drawn at the right of the line; null when unknown.</summary>
        public double? Quality;

        public ItemLine(string text, ItemLineStyle style, string rollStat = null) {
            Text = text;
            Style = style;
            RollStat = rollStat;
        }

        public override string ToString() => $"{Style}: {Text}";
    }
}
