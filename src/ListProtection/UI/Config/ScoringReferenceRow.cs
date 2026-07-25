using System.ComponentModel;

namespace ListProtection.UI.Config
{
    public class ScoringReferenceRow
    {
        [DisplayName("Media Type")]
        public string MediaType { get; set; }

        [DisplayName("Signal Type")]
        public string SignalType { get; set; }

        [DisplayName("Score")]
        public int Score { get; set; }

        [DisplayName("Signal")]
        public string Signal { get; set; }

        [DisplayName("Description")]
        public string Description { get; set; }
    }
}