using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Tier 3 collector — fires only when ContentScore == 0 (no Tier 1 signal matched).
    /// Covers poorly-tagged files and media types without a dedicated collector.
    ///
    /// Name pair and filename pair are each mutually exclusive internally
    /// (stronger suppresses weaker). Name and filename can stack — they are
    /// different fields and constitute independent evidence.
    ///
    /// Facts emitted:
    ///   Fallback.NameExact           — item.Name exact match
    ///   Fallback.NameNormalized      — item.Name after whitespace collapse
    ///   Fallback.FilenameStemExact   — filename stem exact match
    ///   Fallback.FilenameStemNorm    — filename stem after track-prefix strip
    /// </summary>
    public sealed class FallbackEvidenceCollector : IEvidenceCollector
    {
        public string MediaType => null; // applies to all

        public IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate)
        {
            var facts = new List<EvidenceFact>();

            if (gt == null || candidate == null) return facts;

            // Name pair — mutually exclusive
            if (!string.IsNullOrEmpty(gt.Name) && !string.IsNullOrEmpty(candidate.Name))
            {
                if (string.Equals(gt.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))
                    facts.Add(new EvidenceFact(FallbackFacts.NameExact));
                else if (string.Equals(Normalize(gt.Name), Normalize(candidate.Name), StringComparison.OrdinalIgnoreCase))
                    facts.Add(new EvidenceFact(FallbackFacts.NameNormalized));
            }

            // Filename stem pair — mutually exclusive
            var gtStem = GetStem(gt.Path);
            var candidateStem = candidate.FileNameWithoutExtension ?? GetStem(candidate.Path);

            if (!string.IsNullOrEmpty(gtStem) && !string.IsNullOrEmpty(candidateStem))
            {
                if (string.Equals(gtStem, candidateStem, StringComparison.OrdinalIgnoreCase))
                    facts.Add(new EvidenceFact(FallbackFacts.FilenameStemExact));
                else if (string.Equals(StripTrackPrefix(gtStem), StripTrackPrefix(candidateStem), StringComparison.OrdinalIgnoreCase))
                    facts.Add(new EvidenceFact(FallbackFacts.FilenameStemNorm));
            }

            return facts;
        }

        private static string Normalize(string s)
            => string.IsNullOrEmpty(s) ? string.Empty : Regex.Replace(s.Trim(), @"\s+", " ").ToLowerInvariant();

        private static string GetStem(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            try { return Path.GetFileNameWithoutExtension(path) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static readonly Regex _trackPrefix =
            new Regex(@"^\d{1,3}[\s\.\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static string StripTrackPrefix(string stem)
            => string.IsNullOrEmpty(stem) ? string.Empty : _trackPrefix.Replace(stem.Trim(), string.Empty).ToLowerInvariant();
    }

    public static class FallbackFacts
    {
        public const string NameExact = "Fallback.NameExact";
        public const string NameNormalized = "Fallback.NameNormalized";
        public const string FilenameStemExact = "Fallback.FilenameStemExact";
        public const string FilenameStemNorm = "Fallback.FilenameStemNorm";
    }
}