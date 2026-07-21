using System;
using System.Collections.Generic;

namespace ListProtection.Storage
{
    /// <summary>
    /// A single candidate repair record — one library item that may be a
    /// replacement for a specific missing playlist member.
    ///
    /// Multiple candidates can exist per missing member, ranked by Score.
    /// The highest-scored candidate is the recommended repair.
    /// </summary>
    public class CandidateEntry
    {
        /// <summary>
        /// Playlist Guid "N" format string. Matches key in GroundTruthStore.
        /// </summary>
        public string PlaylistId { get; set; }

        /// <summary>
        /// Playlist name at discovery time. Display only.
        /// </summary>
        public string PlaylistName { get; set; }

        /// <summary>
        /// The missing member this candidate was discovered for.
        /// Member.InternalId is the cross-playlist identity key.
        /// </summary>
        public GroundTruthMember MissingMember { get; set; }

        // ── Candidate item ─────────────────────────────────────────────────

        /// <summary>
        /// InternalId of the candidate library item.
        /// </summary>
        public long CandidateInternalId { get; set; }

        /// <summary>
        /// Guid "N" format string of the candidate library item.
        /// </summary>
        public string CandidateId { get; set; }

        /// <summary>
        /// Name of the candidate library item at discovery time.
        /// </summary>
        public string CandidateName { get; set; }

        /// <summary>
        /// Full file path of the candidate library item at discovery time.
        /// </summary>
        public string CandidatePath { get; set; }

        // ── Scoring ────────────────────────────────────────────────────────

        /// <summary>
        /// Composite score — sum of all matched signal weights.
        /// Higher is better. Zero means no signal matched.
        /// </summary>
        public int Score { get; set; }

        /// <summary>
        /// Human-readable list of the signals that contributed to Score.
        /// e.g. ["FilenameStemExact:100", "NameExact:60"]
        /// </summary>
        public List<string> MatchedSignals { get; set; } = new List<string>();

        // ── Metadata ───────────────────────────────────────────────────────

        /// <summary>
        /// UTC timestamp when this candidate was first discovered.
        /// </summary>
        public DateTime DiscoveredAt { get; set; }

        /// <summary>
        /// UTC timestamp of the most recent score calculation.
        /// Updated on every discovery pass that re-evaluates this candidate.
        /// Used by CandidateRefreshTask to expire and re-score stale candidates.
        /// Null for entries recorded before this field was added.
        /// </summary>
        public DateTime? LastScoredAt { get; set; }
    }
}