using System.Collections.Generic;

namespace PlaylistProtection.Core.Models
{
    public class ScoreResult
    {
        public int Total { get; }
        public List<RuleResult> Breakdown { get; }

        public ScoreResult(int total, List<RuleResult> breakdown)
        {
            Total = total;
            Breakdown = breakdown;
        }
    }
}