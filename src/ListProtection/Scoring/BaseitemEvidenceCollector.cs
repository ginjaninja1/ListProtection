using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Evidence collector for signals applicable to all BaseItem types.
    ///
    /// Always runs regardless of MediaType. Media-type-specific collectors
    /// (AudioEvidenceCollector, EpisodeEvidenceCollector, MovieEvidenceCollector)
    /// chain on top.
    ///
    /// Signals fired:
    ///   NameExact              — item.Name exact match, case-insensitive
    ///   NameNormalized         — item.Name after whitespace collapse, case-insensitive
    ///   FilenameStemExact      — filename without extension, exact, case-insensitive
    ///   FilenameStemNormalized — filename stem after stripping track-number prefix
    ///   ParentFolderMatch      — immediate parent folder name, case-insensitive
    ///   GrandparentFolderMatch — two levels up, case-insensitive
    ///
    /// NameExact and NameNormalized are mutually exclusive — only the stronger fires.
    /// FilenameStemExact and FilenameStemNormalized are mutually exclusive similarly.
    ///
    /// Null/empty fields in the GT snapshot do not fire their signal.
    /// Collectors must never throw.
    /// </summary>
    public sealed class BaseItemEvidenceCollector : IEvidenceCollector
    {
        public string MediaType => null; // applies to all

        public IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate)
        {
            var facts = new List<EvidenceFact>();

            if (gt == null || candidate == null) return facts;

            // ── Name signals (mutually exclusive) ─────────────────────────

            if (!string.IsNullOrEmpty(gt.Name) && !string.IsNullOrEmpty(candidate.Name))
            {
                if (string.Equals(gt.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.NameExact)));
                else if (string.Equals(Normalize(gt.Name), Normalize(candidate.Name), StringComparison.OrdinalIgnoreCase))
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.NameNormalized)));
            }

            // ── Filename stem signals (mutually exclusive) ─────────────────

            var gtStem = GetStem(gt.Path);
            var candidateStem = candidate.FileNameWithoutExtension
                             ?? GetStem(candidate.Path);

            if (!string.IsNullOrEmpty(gtStem) && !string.IsNullOrEmpty(candidateStem))
            {
                if (string.Equals(gtStem, candidateStem, StringComparison.OrdinalIgnoreCase))
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.FilenameStemExact)));
                else if (string.Equals(StripTrackPrefix(gtStem), StripTrackPrefix(candidateStem), StringComparison.OrdinalIgnoreCase))
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.FilenameStemNormalized)));
            }

            // ── Folder signals (additive) ──────────────────────────────────

            var gtDir = GetDirectory(gt.Path);
            var candidateDir = GetDirectory(candidate.Path);

            if (!string.IsNullOrEmpty(gtDir) && !string.IsNullOrEmpty(candidateDir))
            {
                var gtParent = Path.GetFileName(gtDir);
                var candidateParent = Path.GetFileName(candidateDir);

                if (!string.IsNullOrEmpty(gtParent) && !string.IsNullOrEmpty(candidateParent) &&
                    string.Equals(gtParent, candidateParent, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.ParentFolderMatch)));

                    // Grandparent — only check if parent already matched
                    var gtGrandparent = Path.GetFileName(Path.GetDirectoryName(gtDir) ?? string.Empty);
                    var candidateGrandparent = Path.GetFileName(Path.GetDirectoryName(candidateDir) ?? string.Empty);

                    if (!string.IsNullOrEmpty(gtGrandparent) && !string.IsNullOrEmpty(candidateGrandparent) &&
                        string.Equals(gtGrandparent, candidateGrandparent, StringComparison.OrdinalIgnoreCase))
                    {
                        facts.Add(new EvidenceFact(nameof(ScoringWeights.GrandparentFolderMatch)));
                    }
                }
            }

            return facts;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return Regex.Replace(s.Trim(), @"\s+", " ").ToLowerInvariant();
        }

        private static string GetStem(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            try { return Path.GetFileNameWithoutExtension(path) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string GetDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            try { return Path.GetDirectoryName(path) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static readonly Regex _trackPrefixRegex =
            new Regex(@"^\d{1,3}[\s\.\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static string StripTrackPrefix(string stem)
        {
            if (string.IsNullOrEmpty(stem)) return string.Empty;
            return _trackPrefixRegex.Replace(stem.Trim(), string.Empty).ToLowerInvariant();
        }
    }
}