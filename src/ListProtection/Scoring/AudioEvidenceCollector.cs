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
    /// Emits atomic facts for Audio items. No combination logic — that lives in CandidateScorer.
    ///
    /// Atomic facts emitted:
    ///   MbTrackIdMatch   — MusicBrainz Track ID exact match
    ///   NameMatch        — item.Name case-insensitive exact
    ///   ArtistMatch      — any artist matches GT primary artist
    ///   AlbumMatch       — item.Album exact match
    ///   TrackNumberMatch — item.IndexNumber matches GT IndexNumber
    ///   DurationMatch    — RunTimeTicks within tolerance
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

            // MusicBrainz Track ID
            var gtMbId = gt.MusicBrainzTrackId;
            var candidateMbId = GetProviderId(audio, MetadataProviders.MusicBrainzTrack);
            if (!string.IsNullOrEmpty(gtMbId) &&
                !string.IsNullOrEmpty(candidateMbId) &&
                string.Equals(gtMbId, candidateMbId, StringComparison.OrdinalIgnoreCase))
            {
                facts.Add(new EvidenceFact(AudioFacts.MbTrackIdMatch));
                return facts; // definitive — no further facts needed
            }

            if (!string.IsNullOrEmpty(gt.Name) &&
                string.Equals(gt.Name, audio.Name, StringComparison.OrdinalIgnoreCase))
                facts.Add(new EvidenceFact(AudioFacts.NameMatch));

            var gtArtist = gt.Artists != null && gt.Artists.Count > 0 ? gt.Artists[0] : null;
            if (!string.IsNullOrEmpty(gtArtist) &&
                (audio.Artists ?? Array.Empty<string>()).Any(a =>
                    string.Equals(a, gtArtist, StringComparison.OrdinalIgnoreCase)))
                facts.Add(new EvidenceFact(AudioFacts.ArtistMatch));

            if (!string.IsNullOrEmpty(gt.Album) &&
                !string.IsNullOrEmpty(audio.Album) &&
                string.Equals(gt.Album, audio.Album, StringComparison.OrdinalIgnoreCase))
                facts.Add(new EvidenceFact(AudioFacts.AlbumMatch));

            if (gt.IndexNumber.HasValue && gt.IndexNumber.Value > 0 &&
                audio.IndexNumber.HasValue &&
                gt.IndexNumber.Value == audio.IndexNumber.Value)
                facts.Add(new EvidenceFact(AudioFacts.TrackNumberMatch));

            if (gt.RunTimeTicks.HasValue && gt.RunTimeTicks.Value > 0 &&
                audio.RunTimeTicks.HasValue && audio.RunTimeTicks.Value > 0 &&
                Math.Abs(gt.RunTimeTicks.Value - audio.RunTimeTicks.Value) <= _durationToleranceTicks)
                facts.Add(new EvidenceFact(AudioFacts.DurationMatch));

            return facts;
        }

        private static string GetProviderId(Audio audio, MetadataProviders provider)
        {
            try
            {
                var ids = audio.ProviderIds;
                if (ids == null) return null;
                return ids.TryGetValue(provider.ToString(), out var val) ? val : null;
            }
            catch { return null; }
        }
    }

    /// <summary>Signal name constants for AudioEvidenceCollector facts.</summary>
    public static class AudioFacts
    {
        public const string MbTrackIdMatch = "MbTrackIdMatch";
        public const string NameMatch = "Audio.NameMatch";
        public const string ArtistMatch = "Audio.ArtistMatch";
        public const string AlbumMatch = "Audio.AlbumMatch";
        public const string TrackNumberMatch = "Audio.TrackNumberMatch";
        public const string DurationMatch = "Audio.DurationMatch";
    }
}