using System;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class DescribeRequestsTests {
        private static readonly DateTime Start = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

        [TestMethod]
        public void SendsTwentyAtATimeAndOneRequestAtATime() {
            var requests = new DescribeRequests(Timeout);

            for (uint guid = 1; guid <= 25; guid++) {
                requests.Ask(guid, Start);
            }

            var first = requests.Next(Start);
            Assert.AreEqual(DescribeRequests.Batch, first.Length);
            Assert.IsNull(requests.Next(Start.AddSeconds(1)), "the first is still waiting on its reply");

            requests.Answered(first, Start.AddSeconds(1));
            CollectionAssert.AreEqual(new uint[] { 21, 22, 23, 24, 25 }, requests.Next(Start.AddSeconds(1)));
        }

        [TestMethod]
        public void ARequestWithNoReplyIsSentAgainThenGivenUpOn() {
            var requests = new DescribeRequests(Timeout);
            requests.Ask(7, Start);
            var at = Start;

            for (var i = 0; i < DescribeRequests.MaxTries; i++) {
                CollectionAssert.AreEqual(new uint[] { 7 }, requests.Next(at), $"try {i + 1}");
                Assert.AreEqual(DescribeState.Waiting, requests.State(7));
                at += Timeout;
            }

            Assert.IsNull(requests.Next(at));
            Assert.AreEqual(DescribeState.NoAnswer, requests.State(7));

            // asked about again too soon does nothing; later, it starts over
            requests.Ask(7, at + TimeSpan.FromSeconds(1));
            Assert.AreEqual(DescribeState.NoAnswer, requests.State(7));
            requests.Ask(7, at + DescribeRequests.RetryAfter);
            Assert.AreEqual(DescribeState.Waiting, requests.State(7));
        }

        [TestMethod]
        public void AnItemTheReplyLeftOutIsNotYours() {
            var requests = new DescribeRequests(Timeout);
            requests.Ask(1, Start);
            requests.Ask(2, Start);
            requests.Next(Start);

            requests.Answered(new uint[] { 1 }, Start.AddSeconds(1));

            Assert.AreEqual(DescribeState.None, requests.State(1));
            Assert.AreEqual(DescribeState.NotYours, requests.State(2));

            // the bank opened again, say: asked about later, it's asked for again
            requests.Ask(2, Start.AddSeconds(1) + DescribeRequests.RetryAfter);
            CollectionAssert.AreEqual(new uint[] { 2 }, requests.Next(Start.AddSeconds(7)));
        }

        [TestMethod]
        public void AnEmptyReplyLeavesEveryItemOut() {
            var requests = new DescribeRequests(Timeout);
            requests.Ask(1, Start);
            requests.Next(Start);

            requests.Answered(new uint[0], Start.AddSeconds(1));

            Assert.AreEqual(DescribeState.NotYours, requests.State(1));
        }

        [TestMethod]
        public void ALateReplyToAnOlderRequestSaysNothingAboutTheNewOne() {
            var requests = new DescribeRequests(Timeout);
            requests.Ask(1, Start);
            requests.Next(Start);

            // 1 timed out and was sent again with 2; then the first request's reply came
            requests.Ask(2, Start.AddSeconds(1));
            var second = requests.Next(Start + Timeout);
            CollectionAssert.AreEquivalent(new uint[] { 1, 2 }, second);

            requests.Answered(new uint[] { 1 }, Start.AddSeconds(4));
            Assert.AreEqual(DescribeState.None, requests.State(1));
            Assert.AreEqual(DescribeState.NotYours, requests.State(2), "1 was in the request, so this was its answer");

            requests.Ask(3, Start.AddSeconds(4));
            requests.Next(Start.AddSeconds(4));
            requests.Answered(new uint[] { 1 }, Start.AddSeconds(5));
            Assert.AreEqual(DescribeState.Waiting, requests.State(3), "a reply about none of its items isn't its answer");
        }

        [TestMethod]
        public void ClearForgetsEverything() {
            var requests = new DescribeRequests(Timeout);
            requests.Ask(1, Start);
            requests.Next(Start);
            requests.Answered(new uint[0], Start);

            requests.Clear();

            Assert.AreEqual(DescribeState.None, requests.State(1));
            requests.Ask(1, Start);
            Assert.IsTrue(requests.Next(Start).SequenceEqual(new uint[] { 1 }));
        }
    }
}
