using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Evidence collector for audio-specific metadata signals.
    /// Only runs when GroundTruthMember.MediaType == "Audio".
    ///
    /// Signals fired:
    ///   ArtistExact           — GT Artists[0] appears as exact element of candidate.Artists
    ///   AlbumExact            — item.Album matches gt.Album, case-insensitive
    ///   AlbumArtistExact      — item.AlbumArtists[0] matches gt.AlbumArtist, case-insensitive
    ///   IndexOnSameAlbum      — track number matches AND album also matches (stronger)
    ///   IndexOnDifferentAlbum — track number matches, album differs (weaker)
    ///   DurationMatch         — |RunTimeTicks delta| within 3-second tolerance
    ///
    /// Index signals are mutually exclusive — only the stronger fires when applicable.
    ///
    /// ArtistExact is a SCORING signal only. The auto-repair gate
    /// (AudioAutoRepairEligibility) independently enforces artist match as a hard
    /// precondition — the score is used only for ranking candidates for the user.
    ///
    /// Fields that are null/empty/zero in the GT snapshot do not fire their signal.
    /// Absence of metadata is not evidence of a mismatch.
    ///
    /// This collector chains on top of BaseItemEvidenceCollector — it does not
    /// re-evaluate name or path signals.
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

            if (!(candidate is Audio audio))
                return facts;

            // ── Artist (exact membership) ──────────────────────────────────
            // GT Artists[0] must appear as an exact element of candidate.Artists.
            // Additional artists on the candidate are not a disqualification.

            var gtArtist = GetPrimaryArtist(gt.Artists);
            if (!string.IsNullOrEmpty(gtArtist))
            {
                var candidateArtists = GetArtistList(audio);
                if (candidateArtists.Any(a =>
                    string.Equals(a, gtArtist, StringComparison.OrdinalIgnoreCase)))
                {
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.ArtistExact)));
                }
            }

            // ── Album ──────────────────────────────────────────────────────

            var albumMatches = !string.IsNullOrEmpty(gt.Album) &&
                               !string.IsNullOrEmpty(audio.Album) &&
                               string.Equals(gt.Album, audio.Album, StringComparison.OrdinalIgnoreCase);

            if (albumMatches)
                facts.Add(new EvidenceFact(nameof(ScoringWeights.AlbumExact)));

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

            // ── Index number (mutually exclusive — same album is stronger) ─

            if (gt.IndexNumber.HasValue && gt.IndexNumber.Value > 0 &&
                audio.IndexNumber.HasValue &&
                gt.IndexNumber.Value == audio.IndexNumber.Value)
            {
                facts.Add(albumMatches
                    ? new EvidenceFact(nameof(ScoringWeights.IndexOnSameAlbum))
                    : new EvidenceFact(nameof(ScoringWeights.IndexOnDifferentAlbum)));
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

        private static string GetPrimaryArtist(List<string> artists)
        {
            if (artists == null || artists.Count == 0) return null;
            return artists[0];
        }

        private static List<string> GetArtistList(Audio audio)
        {
            try
            {
                var artists = audio.Artists;
                if (artists == null || artists.Length == 0) return new List<string>();
                return artists
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();
            }
            catch { return new List<string>(); }
        }

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