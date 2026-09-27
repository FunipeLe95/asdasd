namespace Voron.Characters
{
    // Mouth timing for the Talk clip. Tools/Blender previews mirror these constants exactly.
    public static class FacePerformance
    {
        private static readonly double[] SpeechWindowStarts = { 0.10, 0.56 };
        private static readonly double[] SpeechWindowEnds = { 0.44, 0.86 };

        // Even indices hold the mouth open, odd indices closed; the pattern repeats inside each window.
        private static readonly double[] SyllableSeconds = { 0.12, 0.07, 0.09, 0.06, 0.15, 0.08, 0.10, 0.05, 0.13, 0.09 };
        private static readonly double SyllableCycleSeconds = Sum(SyllableSeconds);

        // Double math matches the Python float arithmetic, so frames that land exactly on a
        // syllable boundary resolve the same way in Unity and in the Blender previews.
        public static bool IsMouthOpen(float normalizedTime, float clipLength)
        {
            if (!(clipLength > 0f))
                return false;

            double time = normalizedTime;
            for (int window = 0; window < SpeechWindowStarts.Length; window++)
            {
                if (time < SpeechWindowStarts[window] || time >= SpeechWindowEnds[window])
                    continue;

                double seconds = ((time - SpeechWindowStarts[window]) * clipLength) % SyllableCycleSeconds;
                for (int syllable = 0; syllable < SyllableSeconds.Length; syllable++)
                {
                    if (seconds < SyllableSeconds[syllable])
                        return syllable % 2 == 0;
                    seconds -= SyllableSeconds[syllable];
                }
                return false;
            }

            return false;
        }

        private static double Sum(double[] values)
        {
            double total = 0.0;
            foreach (double value in values)
                total += value;
            return total;
        }
    }
}
