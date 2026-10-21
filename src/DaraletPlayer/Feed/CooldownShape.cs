using System;
using System.Collections.Generic;
using System.Globalization;

namespace DaraletPlayer.Feed {
    /// <summary>The shapes a slot's cooldown is drawn with, worked out in a square one unit across.</summary>
    public static class CooldownShape {
        /// <summary>
        /// The clock sweep's dark wedge, from the hand (elapsed of the way round, clockwise from the top) back round to the
        /// top, as points along the square's edge. Each neighbouring pair makes a triangle with the centre.
        /// </summary>
        public static List<(float X, float Y)> Sweep(float elapsed) {
            var points = new List<(float, float)>();
            elapsed = Math.Max(0, Math.Min(1, elapsed));

            if (elapsed >= 1) {
                return points;
            }

            var start = elapsed * Math.PI * 2;
            points.Add(Edge(start));

            // the square's corners the wedge reaches round, then the top again
            foreach (var corner in new[] { 0.25, 0.75, 1.25, 1.75 }) {
                var angle = corner * Math.PI;

                if (angle > start) {
                    points.Add(Edge(angle));
                }
            }

            points.Add(Edge(Math.PI * 2));
            return points;
        }

        /// <summary>Where a line from the centre at an angle (0 straight up, clockwise) meets the square's edge.</summary>
        private static (float X, float Y) Edge(double angle) {
            var x = Math.Sin(angle);
            var y = -Math.Cos(angle);
            var far = Math.Max(Math.Abs(x), Math.Abs(y));
            return ((float)(0.5 + 0.5 * x / far), (float)(0.5 + 0.5 * y / far));
        }

        /// <summary>A cooldown's time left in a few characters: "1h", "3m", "12", "4.5" (or "5", without decimals).</summary>
        public static string TimeLeft(double seconds, bool decimals = true) {
            if (seconds >= 3600) {
                return $"{Math.Ceiling(seconds / 3600)}h";
            }

            if (seconds >= 60) {
                return $"{Math.Ceiling(seconds / 60)}m";
            }

            if (seconds >= 10 || !decimals) {
                return $"{Math.Ceiling(seconds)}";
            }

            return (Math.Ceiling(seconds * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
