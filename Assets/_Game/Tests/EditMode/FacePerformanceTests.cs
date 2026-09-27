using NUnit.Framework;
using Voron.Characters;

namespace Voron.Tests
{
    public sealed class FacePerformanceTests
    {
        private const float TalkLength = 4f;
        private const float FirstWindowStart = 0.10f;
        private const float FirstWindowEnd = 0.44f;
        private const float SecondWindowStart = 0.56f;
        private const float SecondWindowEnd = 0.86f;

        [TestCase(0f)]
        [TestCase(0.05f)]
        [TestCase(0.5f)]
        [TestCase(0.95f)]
        public void MouthIsClosedBeforeBetweenAndAfterSpeech(float normalizedTime)
        {
            Assert.That(FacePerformance.IsMouthOpen(normalizedTime, TalkLength), Is.False);
        }

        [Test]
        public void MouthNeverOpensOutsideTheSpeechWindows()
        {
            for (int step = 0; step <= 2000; step++)
            {
                float normalizedTime = step / 2000f;
                // Classify the exact value of the float input: 0.44f is just below 0.44, i.e. still inside.
                double exact = normalizedTime;
                bool inWindow = (exact >= 0.10 && exact < 0.44) || (exact >= 0.56 && exact < 0.86);
                if (!inWindow)
                    Assert.That(FacePerformance.IsMouthOpen(normalizedTime, TalkLength), Is.False, "t=" + normalizedTime);
            }
        }

        [Test]
        public void MouthOpensAndClosesSeveralTimesInsideEachSpeechWindow()
        {
            Assert.That(CountMouthOpenings(FirstWindowStart, FirstWindowEnd), Is.GreaterThanOrEqualTo(4));
            Assert.That(CountMouthOpenings(SecondWindowStart, SecondWindowEnd), Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void MouthFollowsTheSyllableTimingSharedWithBlenderPreviews()
        {
            // Syllables alternate open/closed: 0.12 open, 0.07 closed, 0.09 open ... repeating every 0.94 s.
            Assert.That(IsOpenAt(FirstWindowStart, 0f), Is.True);
            Assert.That(IsOpenAt(FirstWindowStart, 0.05f), Is.True);
            Assert.That(IsOpenAt(FirstWindowStart, 0.15f), Is.False);
            Assert.That(IsOpenAt(FirstWindowStart, 0.22f), Is.True);
            Assert.That(IsOpenAt(FirstWindowStart, 0.30f), Is.False);
            Assert.That(IsOpenAt(FirstWindowStart, 0.94f + 0.05f), Is.True);
            Assert.That(IsOpenAt(FirstWindowStart, 0.94f + 0.15f), Is.False);
            Assert.That(IsOpenAt(SecondWindowStart, 0.05f), Is.True);
            Assert.That(IsOpenAt(SecondWindowStart, 0.15f), Is.False);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void NonPositiveClipLengthKeepsTheMouthClosed(float clipLength)
        {
            foreach (float normalizedTime in new[] { 0.1f, 0.2f, 0.3f, 0.6f, 0.7f })
                Assert.That(FacePerformance.IsMouthOpen(normalizedTime, clipLength), Is.False, "t=" + normalizedTime);
        }

        private static bool IsOpenAt(float windowStart, float secondsIntoWindow)
        {
            return FacePerformance.IsMouthOpen(windowStart + secondsIntoWindow / TalkLength, TalkLength);
        }

        private static int CountMouthOpenings(float windowStart, float windowEnd)
        {
            int openings = 0;
            bool wasOpen = false;
            // Step well below the shortest 0.05 s syllable.
            for (float seconds = 0f; windowStart + seconds / TalkLength < windowEnd; seconds += 0.005f)
            {
                bool open = IsOpenAt(windowStart, seconds);
                if (open && !wasOpen)
                    openings++;
                wasOpen = open;
            }
            return openings;
        }
    }
}
