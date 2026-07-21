using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Produces EvidenceFacts by comparing a GT member snapshot against a
    /// live candidate BaseItem.
    ///
    /// Implementations are media-type specific:
    ///   BaseItemEvidenceCollector  — path/name signals, applicable to all BaseItems
    ///   AudioEvidenceCollector     — album/artist/track/duration, Audio only
    ///
    /// CandidateDiscoverer selects which collectors to run based on
    /// GroundTruthMember.MediaType. BaseItemEvidenceCollector always runs;
    /// a media-type-specific collector is chained on top when the MediaType matches.
    ///
    /// Collectors must never throw — return an empty list on any error.
    /// </summary>
    public interface IEvidenceCollector
    {
        /// <summary>
        /// The MediaType this collector handles, e.g. "Audio", "Episode", "Movie".
        /// Null means the collector applies to all media types (base collector).
        /// </summary>
        string MediaType { get; }

        /// <summary>
        /// Extracts evidence facts from the comparison of a GT member snapshot
        /// against a live candidate item.
        /// </summary>
        IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate);
    }
}