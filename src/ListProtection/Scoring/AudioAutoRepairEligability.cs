using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Auto-repair eligibility gate for Audio items.
    ///
    /// All three conditions must be true for a candidate to be auto-repaired:
    ///
    ///   1. Name match   — candidate.Name == gt.Name (case-insensitive exact)
    ///   2. Artist match — gt.Artists[0] appears as exact element of candidate.Artists
    ///   3. Album match  — candidate.Album == gt.Album (case-insensitive exact)
    ///
    /// If a GT field is null/empty the corresponding condition is skipped —
    /// absence of metadata in the GT snapshot is not a disqualifier. However if
    /// all three GT fields are absent (legacy entries with no audio metadata),
    /// eligibility returns false to prevent silent repairs on unverifiable matches.
    ///
    /// A candidate that fails is surfaced to the user as a ranked suggestion —
    /// never silently applied.
    /// </summary>
    public sealed class AudioAutoRepairEligibility : IAutoRepairEligibility
    {
        public string MediaType => "Audio";

        public bool IsEligible(GroundTruthMember gt, BaseItem candidate)
        {
            try
            {
                if (gt == null || candidate == null) return false;
                if (!(candidate is Audio audio)) return false;

                var hasName = !string.IsNullOrEmpty(gt.Name);
                var hasArtist = gt.Artists != null && gt.Artists.Count > 0
                                && !string.IsNullOrEmpty(gt.Artists[0]);
                var hasAlbum = !string.IsNullOrEmpty(gt.Album);

                // Legacy GT entry with no audio metadata — cannot verify identity
                if (!hasName && !hasArtist && !hasAlbum) return false;

                // 1. Name
                if (hasName &&
                    !string.Equals(gt.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))
                    return false;

                // 2. Artist — GT primary artist must be an exact element of candidate.Artists
                if (hasArtist)
                {
                    var gtArtist = gt.Artists[0];
                    var candidateArtists = GetCandidateArtists(audio);
                    if (!candidateArtists.Any(a =>
                            string.Equals(a, gtArtist, StringComparison.OrdinalIgnoreCase)))
                        return false;
                }

                // 3. Album
                if (hasAlbum &&
                    !string.Equals(gt.Album, audio.Album, StringComparison.OrdinalIgnoreCase))
                    return false;

                return true;
            }
            catch { return false; }
        }

        private static List<string> GetCandidateArtists(Audio audio)
        {
            try
            {
                var artists = audio.Artists;
                if (artists == null || artists.Length == 0) return new List<string>();
                return artists.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            }
            catch { return new List<string>(); }
        }
    }
}