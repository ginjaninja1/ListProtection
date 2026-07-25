using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Produces atomic EvidenceFacts by observing a GT member snapshot
    /// against a live candidate BaseItem.
    ///
    /// Collectors are dumb observers — they never decide what a fact is worth,
    /// never suppress other facts, never apply combination logic.
    /// All weighting and combination logic lives in CandidateScorer.
    ///
    /// Three collector tiers:
    ///   Tier 1 — Media-type collectors (Audio, Episode, Movie)
    ///            Emit atomic metadata facts. Run when MediaType matches.
    ///   Tier 2 — FolderEvidenceCollector
    ///            Emits depth facts. Always runs. Independent of Tiers 1 and 3.
    ///   Tier 3 — FallbackEvidenceCollector
    ///            Emits name/filename facts. Consulted only when ContentScore == 0.
    ///
    /// Collectors must never throw — return empty list on any error.
    /// </summary>
    public interface IEvidenceCollector
    {
        /// <summary>
        /// MediaType this collector handles e.g. "Audio", "Episode", "Movie".
        /// Null means the collector applies to all media types.
        /// </summary>
        string MediaType { get; }

        IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate);
    }
}