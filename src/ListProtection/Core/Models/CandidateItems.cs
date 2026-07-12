using System;
using System.Collections.Generic;

namespace PlaylistProtection.Core.Models
{
    public class CandidateItem
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Path { get; set; }

        public string ProviderId { get; set; }

        public string MediaType { get; set; }

        // Overall computed confidence score (0–100)
        public int Confidence { get; set; }

        // Per-rule breakdown (debug/explainability)
        public Dictionary<string, int> RuleScores { get; set; }
            = new Dictionary<string, int>();

        // Optional metadata for explainability UI
        public DateTime LastEvaluatedUtc { get; set; }

        // Indicates whether this candidate is currently valid in Emby
        public bool IsActiveInLibrary { get; set; }
    }
}