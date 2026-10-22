using System;
using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The items PlayerFeed asks @playerui describe about, 20 to a request, one request at a time. The server sends nothing
    /// for an item it won't describe, and nothing at all for a request its rate limit drops, so without this an item asked
    /// about once and never answered stayed "Getting the details..." for good. Now a request with no answer is sent again
    /// (MaxTries in all), an item a reply left out is noted as not yours, and either can be asked about again after
    /// RetryAfter. Kept apart from PlayerFeed, which needs Decal, so tests can reach it.
    /// </summary>
    public class DescribeRequests {
        /// <summary>The most items one @playerui describe may name (PlayerUiCorpseData.MaxDescribeItems).</summary>
        public const int Batch = 20;

        /// <summary>How many times a request goes out before its items are given up on.</summary>
        public const int MaxTries = 3;

        /// <summary>An item given up on, or left out of a reply, is asked about again after this long, if still wanted.</summary>
        public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

        private readonly TimeSpan timeout;
        private readonly List<uint> queue = new List<uint>();
        private readonly Dictionary<uint, int> tries = new Dictionary<uint, int>();
        private readonly Dictionary<uint, (DescribeState State, DateTime At)> failed =
            new Dictionary<uint, (DescribeState, DateTime)>();

        private uint[] inFlight;
        private DateTime sentAt = DateTime.MinValue;

        /// <param name="timeout">How long to wait for a reply before sending the request again.</param>
        public DescribeRequests(TimeSpan timeout) {
            this.timeout = timeout;
        }

        /// <summary>When the last request went out.</summary>
        public DateTime SentAt => sentAt;

        /// <summary>Asks about an item (one with no description yet), unless it's already waiting or failed too lately.</summary>
        public void Ask(uint guid, DateTime now) {
            if (guid == 0 || queue.Contains(guid) || (inFlight != null && inFlight.Contains(guid))) {
                return;
            }

            if (failed.TryGetValue(guid, out var failure)) {
                if (now - failure.At < RetryAfter) {
                    return;
                }

                failed.Remove(guid);
            }

            tries.Remove(guid);
            queue.Add(guid);
        }

        /// <summary>Where an item stands; None for one never asked about, or answered.</summary>
        public DescribeState State(uint guid) {
            if (failed.TryGetValue(guid, out var failure)) {
                return failure.State;
            }

            var waiting = queue.Contains(guid) || (inFlight != null && inFlight.Contains(guid));
            return waiting ? DescribeState.Waiting : DescribeState.None;
        }

        /// <summary>
        /// The next request's items, or null while one is waiting on its reply or nothing's queued. A request with no reply
        /// by now goes back to the front of the queue, its items each a try nearer being given up on.
        /// </summary>
        public uint[] Next(DateTime now) {
            if (inFlight != null) {
                if (now - sentAt < timeout) {
                    return null;
                }

                TimedOut(now);
            }

            if (queue.Count == 0) {
                return null;
            }

            inFlight = queue.Take(Batch).ToArray();
            queue.RemoveRange(0, inFlight.Length);
            sentAt = now;

            foreach (var guid in inFlight) {
                tries[guid] = (tries.TryGetValue(guid, out var count) ? count : 0) + 1;
            }

            return inFlight;
        }

        /// <summary>
        /// A describe reply came, with these items. What the request asked about and the reply left out, the server won't
        /// describe. A reply with none of the request's items (one so late its request was sent again since) says nothing
        /// about the request, unless it's empty: then it was the answer, or one as late, and either way the items were
        /// left out.
        /// </summary>
        public void Answered(IEnumerable<uint> described, DateTime now) {
            var got = new HashSet<uint>(described);

            foreach (var guid in got) {
                queue.Remove(guid);
                failed.Remove(guid);
                tries.Remove(guid);
            }

            if (inFlight == null) {
                return;
            }

            if (got.Count > 0 && !inFlight.Any(got.Contains)) {
                return;
            }

            foreach (var guid in inFlight.Where(g => !got.Contains(g))) {
                failed[guid] = (DescribeState.NotYours, now);
                tries.Remove(guid);
            }

            inFlight = null;
        }

        /// <summary>Forgets everything: every item is asked about afresh.</summary>
        public void Clear() {
            queue.Clear();
            tries.Clear();
            failed.Clear();
            inFlight = null;
        }

        private void TimedOut(DateTime now) {
            var again = new List<uint>();

            foreach (var guid in inFlight) {
                if (tries.TryGetValue(guid, out var count) && count >= MaxTries) {
                    failed[guid] = (DescribeState.NoAnswer, now);
                    tries.Remove(guid);
                }
                else {
                    again.Add(guid);
                }
            }

            queue.InsertRange(0, again);
            inFlight = null;
        }
    }
}
