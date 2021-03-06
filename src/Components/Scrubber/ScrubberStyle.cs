using UnityEngine;

namespace VamTimeline
{
    public class ScrubberStyle : StyleBase
    {
        public static ScrubberStyle Default()
        {
            return new ScrubberStyle();
        }

        // Scrubber
        public Color ScrubberColor { get; set; } = new Color(0.88f, 0.84f, 0.86f);
        public float ScrubberSize { get; set; } = 2f;
        public Color SecondsColor { get; set; } = new Color(0.50f, 0.48f, 0.48f);
        public float SecondsSize { get; set; } = 4f;
        public Color SecondFractionsColor { get; set; } = new Color(0.65f, 0.63f, 0.63f);
        public float SecondFractionsSize { get; set; } = 2.5f;
    }
}
