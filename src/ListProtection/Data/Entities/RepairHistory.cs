using System;
using System.Collections.Generic;

namespace PlaylistProtection.Data.Entities
{
    /// <summary>
    /// Persistent audit record of attempted or completed repairs.
    /// Used for tracking confidence decisions over time.
    /// </summary>
    public class RepairHistory
    {
        public string MissingMemberId { get; }
        public string SelectedCandidateId { get; }
        public int ConfidenceScore { get; }
        public DateTime Timestamp { get; }

        /// <summary>
        /// Breakdown of why a candidate was selected.
        /// </summary>
        public Dictionary<string, int> RuleBreakdown { get; }

        public RepairHistory(
            string missingMemberId,
            string selectedCandidateId,
            int confidenceScore,
            Dictionary<string, int> ruleBreakdown)
        {
            MissingMemberId = missingMemberId;
            SelectedCandidateId = selectedCandidateId;
            ConfidenceScore = confidenceScore;
            RuleBreakdown = ruleBreakdown;
            Timestamp = DateTime.UtcNow;
        }
    }
}