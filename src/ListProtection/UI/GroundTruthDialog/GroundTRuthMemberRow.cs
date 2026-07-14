using System.ComponentModel;

namespace ListProtection.UI.GroundTruthDialog
{
    /// <summary>
    /// Read-only row for the Ground Truth dialog grid.
    /// One row per member in the playlist's ground truth snapshot.
    /// </summary>
    public class GroundTruthMemberRow
    {
        [DisplayName("#")]
        public int Position { get; set; }

        [DisplayName("Track")]
        public string Name { get; set; }

        [DisplayName("Path")]
        public string Path { get; set; }
    }
}