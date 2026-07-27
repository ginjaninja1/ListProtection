using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using System;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Emits atomic facts for MusicAlbum items used directly as collection members
    /// (a whole album added to a Collection, as distinct from individual Audio
    /// tracks inside a protected playlist — see AudioEvidenceCollector for that case).
    ///
    /// Deliberately conservative: only base fields already proven safe (Name,
    /// ProductionYear — captured for every item type in GroundTruthMemberFactory's
    /// base block) are used. No MusicBrainz release/album-group provider ID is
    /// referenced here — that enum member is not confirmed to exist in this SDK
    /// version. If you want a definitive-match signal added (equivalent to
    /// MusicBrainz Track ID for Audio), get an ILSpy dump of the MetadataProviders
    /// enum first and this collector can be extended with a short-circuit rule.
    ///
    /// Atomic facts emitted:
    ///   TitleMatch  — album name exact match
    ///   YearMatch   — production year matches
    /// </summary>
    public sealed class MusicAlbumEvidenceCollector : IEvidenceCollector
    {
        public string MediaType => "MusicAlbum";

        public IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate)
        {
            var facts = new List<EvidenceFact>();

            if (gt == null || candidate == null) return facts;
            if (!string.Equals(candidate.GetType().Name, "MusicAlbum", StringComparison.Ordinal)) return facts;

            if (!string.IsNullOrEmpty(gt.Name) &&
                !string.IsNullOrEmpty(candidate.Name) &&
                string.Equals(gt.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))
                facts.Add(new EvidenceFact(MusicAlbumFacts.TitleMatch));

            if (gt.ProductionYear.HasValue &&
                candidate.ProductionYear.HasValue &&
                gt.ProductionYear.Value == candidate.ProductionYear.Value)
                facts.Add(new EvidenceFact(MusicAlbumFacts.YearMatch));

            return facts;
        }
    }

    public static class MusicAlbumFacts
    {
        public const string TitleMatch = "MusicAlbum.TitleMatch";
        public const string YearMatch = "MusicAlbum.YearMatch";
    }
}