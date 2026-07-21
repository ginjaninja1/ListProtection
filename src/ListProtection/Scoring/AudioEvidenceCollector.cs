using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using System;
using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Evidence collector for audio-specific metadata signals.
    /// Only runs when GroundTruthMember.MediaType == "Audio".
    ///
    /// Signals fired:
    ///   AlbumExact       — item.Album matches gt.Album, case-insensitive
    ///   AlbumArtistExact — item.AlbumArtists[0] matches gt.AlbumArtist, case-insensitive
    ///   IndexNumberMatch — item.IndexNumber == gt.IndexNumber (track number)
    ///   DurationMatch    — |RunTimeTicks delta| within a 3-second tolerance
    ///
    /// All four fields must be captured on GroundTruthMember at snapshot time.
    /// Fields that are null/empty/zero in the GT snapshot do not fire their signal —
    /// absence of metadata is not evidence of a mismatch.
    ///
    /// This collector chains on top of BaseItemEvidenceCollector — it does not
    /// re-evaluate name or path signals.
    ///
    /// The upgrade scenario (MP3 → FLAC) is the primary motivation for this
    /// collector: NameExact(60) + AlbumExact(50) + AlbumArtistExact(30) = 140
    /// even when the filename stem changes, giving confident candidate selection
    /// without relying solely on the filename.
    /// </summary>
    public sealed class AudioEvidenceCollector : IEvidenceCollector
    {
        // 3-second tolerance in ticks (1 tick = 100 nanoseconds)
        private const long DurationToleranceTicks = 3L * 10_000_000L;

        /// <inheritdoc/>
        public string MediaType => "Audio";

        /// <inheritdoc/>
        public IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate)
        {
            var facts = new List<EvidenceFact>();

            if (gt == null || candidate == null)
                return facts;

            // Only meaningful against an Audio item
            if (!(candidate is Audio audio))
                return facts;

            // ── Album ──────────────────────────────────────────────────────

            if (!string.IsNullOrEmpty(gt.Album) && !string.IsNullOrEmpty(audio.Album) &&
                string.Equals(gt.Album, audio.Album, StringComparison.OrdinalIgnoreCase))
            {
                facts.Add(new EvidenceFact(nameof(ScoringWeights.AlbumExact)));
            }

            // ── Album artist ───────────────────────────────────────────────

            if (!string.IsNullOrEmpty(gt.AlbumArtist))
            {
                var candidateAlbumArtist = GetFirstAlbumArtist(audio);
                if (!string.IsNullOrEmpty(candidateAlbumArtist) &&
                    string.Equals(gt.AlbumArtist, candidateAlbumArtist, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.AlbumArtistExact)));
                }
            }

            // ── Track number (IndexNumber) ─────────────────────────────────

            if (gt.IndexNumber.HasValue && gt.IndexNumber.Value > 0 &&
                audio.IndexNumber.HasValue &&
                gt.IndexNumber.Value == audio.IndexNumber.Value)
            {
                facts.Add(new EvidenceFact(nameof(ScoringWeights.IndexNumberMatch)));
            }

            // ── Duration ──────────────────────────────────────────────────

            if (gt.RunTimeTicks.HasValue && gt.RunTimeTicks.Value > 0 &&
                audio.RunTimeTicks.HasValue && audio.RunTimeTicks.Value > 0)
            {
                var delta = Math.Abs(gt.RunTimeTicks.Value - audio.RunTimeTicks.Value);
                if (delta <= DurationToleranceTicks)
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.DurationMatch)));
            }

            return facts;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static string GetFirstAlbumArtist(Audio audio)
        {
            try
            {
                var artists = audio.AlbumArtists;
                if (artists == null || artists.Length == 0) return string.Empty;
                return artists[0] ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}