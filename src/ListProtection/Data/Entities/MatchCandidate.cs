using System.Collections.Generic;

namespace PlaylistProtection.Data.Entities
{
    /// <summary>
    /// Represents a potential recovery match for a MissingMember.
    /// Produced from Emby library search or lookup APIs.
    /// </summary>
    public class MatchCandidate
    {
        public string Id { get; }
        public string Name { get; }
        public string Path { get; }

        /// <summary>
        /// Final aggregated confidence score from ConfidenceEngine.
        /// </summary>
        public int Confidence { get; set; }

        /// <summary>
        /// Breakdown of per-rule scoring contributions.
        /// </summary>
        public Dictionary<string, int> RuleScores { get; }

        public MatchCandidate(string id, string name, string path)
        {
            Id = id;
            Name = name;
            Path = path;
            RuleScores = new Dictionary<string, int>();
        }
    }
}