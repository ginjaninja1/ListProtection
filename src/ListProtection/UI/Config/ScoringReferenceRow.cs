using System.ComponentModel;

namespace ListProtection.UI.Config
{
    /// <summary>
    /// A single row in the scoring reference dialog grid.
    /// Column widths are applied via post-processing in ScoringReferenceDialogUI.Build.
    /// </summary>
    public class ScoringReferenceRow
    {
        [DisplayName("Media Type")]
        public string MediaType { get; set; }

        [DisplayName("Score")]
        public int Score { get; set; }

        [DisplayName("Signal")]
        public string Signal { get; set; }

        [DisplayName("Description")]
        public string Description { get; set; }
    }
}