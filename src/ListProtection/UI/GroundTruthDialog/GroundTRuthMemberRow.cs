using System.ComponentModel;

namespace ListProtection.UI.GroundTruthDialog
{
    public class GroundTruthMemberRow
    {
        [DisplayName("#")]
        public int Position { get; set; }

        [DisplayName("Type")]
        public string MediaType { get; set; }

        [DisplayName("Member")]
        public string Name { get; set; }

        [DisplayName("Path")]
        public string Path { get; set; }
    }
}