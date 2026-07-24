using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Evidence collector for audio-specific combination signals.
    /// Chains on top of BaseItemEvidenceCollector (name/path signals are handled there).
    /// Only runs when GroundTruthMember.MediaType == "Audio".
    ///
    /// Signal hierarchy (highest applicable combination fires; lower tiers do not re-fire
    /// for the same field set). Folder/filename signals are additive on top.
    ///
    ///   MbTrackIdMatch         200  MusicBrainz Track ID — definitive; returns immediately
    ///   NameArtistAlbumIndex   170  Name + Artist + Album + TrackNumber
    ///   NameArtistAlbum        150  Name + Artist + Album
    ///   NameArtistDuration     140  Name + Artist + Duration within tolerance
    ///   NameAlbumIndex         120  Name + Album + TrackNumber
    ///   DurationAlbumIndex     110  Duration + Album + TrackNumber
    ///   NameArtist              80  Name + Artist (no album)
    ///   NameDuration            70  Name + Duration
    ///   AlbumIndex              50  Album + TrackNumber
    ///   NameOnly                30  Name alone
    ///   ArtistAlbum             25  Artist + Album (no name)
    ///
    /// Duration tolerance injected at construction from
    /// PluginConfiguration.AudioDurationToleranceSeconds (default 2s).
    ///
    /// Fields null/empty/zero in the GT snapshot do not contribute to combinations.
    /// </summary>
    public sealed class AudioEvidenceCollector : IEvidenceCollector
    {
        private readonly long _durationToleranceTicks;

        public AudioEvidenceCollector(int durationToleranceSeconds = 2)
        {
            _durationToleranceTicks = (long)durationToleranceSeconds * 10_000_000L;
        }

        public string MediaType => "Audio";

        public IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate)
        {
            var facts = new List<EvidenceFact>();

            if (gt == null || candidate == null) return facts;
            if (!(candidate is Audio audio)) return facts;

            // ── Primitive signals ──────────────────────────────────────────

            // MusicBrainz Track ID
            var gtMbId = gt.MusicBrainzTrackId;
            var candidateMbId = GetProviderId(audio, MetadataProviders.MusicBrainzTrack);
            var mbMatch = !string.IsNullOrEmpty(gtMbId) &&
                          !string.IsNullOrEmpty(candidateMbId) &&
                          string.Equals(gtMbId, candidateMbId, StringComparison.OrdinalIgnoreCase);

            if (mbMatch)
            {
                facts.Add(new EvidenceFact(nameof(ScoringWeights.MbTrackIdMatch)));
                return facts; // definitive — nothing else needed
            }

            var nameMatch = !string.IsNullOrEmpty(gt.Name) &&
                            string.Equals(gt.Name, audio.Name, StringComparison.OrdinalIgnoreCase);

            var gtArtist = gt.Artists != null && gt.Artists.Count > 0 ? gt.Artists[0] : null;
            var artistMatch = !string.IsNullOrEmpty(gtArtist) &&
                              (audio.Artists ?? Array.Empty<string>()).Any(a =>
                                  string.Equals(a, gtArtist, StringComparison.OrdinalIgnoreCase));

            var albumMatch = !string.IsNullOrEmpty(gt.Album) &&
                             !string.IsNullOrEmpty(audio.Album) &&
                             string.Equals(gt.Album, audio.Album, StringComparison.OrdinalIgnoreCase);

            var indexMatch = gt.IndexNumber.HasValue && gt.IndexNumber.Value > 0 &&
                             audio.IndexNumber.HasValue &&
                             gt.IndexNumber.Value == audio.IndexNumber.Value;

            var durationMatch = gt.RunTimeTicks.HasValue && gt.RunTimeTicks.Value > 0 &&
                                audio.RunTimeTicks.HasValue && audio.RunTimeTicks.Value > 0 &&
                                Math.Abs(gt.RunTimeTicks.Value - audio.RunTimeTicks.Value) <= _durationToleranceTicks;

            // ── Combination signals (highest applicable fires) ─────────────

            if (nameMatch && artistMatch && albumMatch && indexMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.NameArtistAlbumIndex)));
            else if (nameMatch && artistMatch && albumMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.NameArtistAlbum)));
            else if (nameMatch && artistMatch && durationMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.NameArtistDuration)));
            else if (nameMatch && albumMatch && indexMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.NameAlbumIndex)));
            else if (durationMatch && albumMatch && indexMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.DurationAlbumIndex)));
            else if (nameMatch && artistMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.NameArtist)));
            else if (nameMatch && durationMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.NameDuration)));
            else if (albumMatch && indexMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.AlbumIndex)));
            else if (nameMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.NameOnly)));
            else if (artistMatch && albumMatch)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.ArtistAlbum)));

            return facts;
        }

        private static string GetProviderId(Audio audio, MetadataProviders provider)
        {
            try
            {
                var ids = audio.ProviderIds;
                if (ids == null) return null;
                var key = provider.ToString();
                return ids.TryGetValue(key, out var val) ? val : null;
            }
            catch { return null; }
        }
    }
}