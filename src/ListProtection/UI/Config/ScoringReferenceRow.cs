using System.ComponentModel;

namespace ListProtection.UI.Config
{
    public class ScoringReferenceRow
    {
        [DisplayName("Tier")]
        public string Tier { get; set; }

        [DisplayName("Score")]
        public int Score { get; set; }

        [DisplayName("Signals Required")]
        public string Signal { get; set; }

        [DisplayName("Notes")]
        public string Notes { get; set; }
    }
}