using ListProtection.Storage;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Determines whether a candidate is eligible for automatic (unattended) repair
    /// of a specific missing playlist member.
    ///
    /// The gate is evaluated AFTER scoring. It receives the full ordered candidate
    /// list so it can enforce a minimum score gap (min-distance) between the best
    /// candidate and the runner-up — preventing auto-repair when two candidates are
    /// equally plausible.
    ///
    /// Implementations are registered in AutoRepairer._eligibilityGates keyed by
    /// MediaType string. A missing member with no registered gate is never
    /// auto-repaired (surfaced for manual review only).
    ///
    /// Implementations must never throw.
    /// </summary>
    public interface IAutoRepairEligibility
    {
        /// <summary>
        /// The MediaType this gate handles, e.g. "Audio", "Episode", "Movie".
        /// </summary>
        string MediaType { get; }

        /// <summary>
        /// Returns true if the top-ranked candidate in <paramref name="rankedCandidates"/>
        /// is eligible for automatic repair.
        ///
        /// <paramref name="rankedCandidates"/> is ordered descending by score. The
        /// implementation is responsible for checking both the hard semantic gate
        /// (metadata conditions) and the score/distance thresholds from config.
        /// </summary>
        /// <param name="gt">The missing member's GT snapshot.</param>
        /// <param name="rankedCandidates">All candidates for this member, best-first.</param>
        /// <param name="threshold">Minimum score for the top candidate.</param>
        /// <param name="minDistance">Minimum score gap between top and second candidate.</param>
        bool IsEligible(
            GroundTruthMember gt,
            IReadOnlyList<ScoredCandidate> rankedCandidates,
            int threshold,
            int minDistance);
    }
}