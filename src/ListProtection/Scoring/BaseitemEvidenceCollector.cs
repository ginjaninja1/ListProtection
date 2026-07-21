using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Evidence collector for signals derivable from any BaseItem's
    /// Name and Path — applicable regardless of media type.
    ///
    /// Signals fired:
    ///   FilenameStemExact      — stem without extension, case-insensitive exact
    ///   FilenameStemNormalized — after stripping leading track-number prefix
    ///   NameExact              — item.Name exact, case-insensitive
    ///   NameNormalized         — whitespace-collapsed lowercase
    ///   ParentFolderMatch      — immediate parent folder name (year-prefix stripped)
    ///   GrandparentFolderMatch — two levels up (artist folder for music, show for TV)
    ///
    /// FilenameStem signals are mutually exclusive — Normalized only fires
    /// when Exact did not. Same for Name signals.
    ///
    /// This collector always runs regardless of MediaType. It is registered
    /// with MediaType = null in CandidateDiscoverer.
    /// </summary>
    public sealed class BaseItemEvidenceCollector : IEvidenceCollector
    {
        // Matches leading track-number prefixes: "02. ", "02 - ", "02-", "2. ", etc.
        private static readonly Regex TrackPrefixRegex =
            new Regex(@"^\d{1,3}[\s\.\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Strips leading year folder annotations: "[2021] ", "(2021) ", "2021 - "
        private static readonly Regex YearFolderRegex =
            new Regex(@"^[\[\(]?\d{4}[\]\)]?[\s\-]+", RegexOptions.Compiled);

        /// <inheritdoc/>
        public string MediaType => null; // applies to all

        /// <inheritdoc/>
        public IEnumerable<EvidenceFact> Collect(GroundTruthMember gt, BaseItem candidate)
        {
            var facts = new List<EvidenceFact>();

            if (gt == null || candidate == null)
                return facts;

            // ── Filename stem signals ──────────────────────────────────────

            var gtStem = GetStem(gt.Path);
            var candidateStem = candidate.FileNameWithoutExtension ?? string.Empty;

            if (!string.IsNullOrEmpty(gtStem) && !string.IsNullOrEmpty(candidateStem))
            {
                if (string.Equals(gtStem, candidateStem, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.FilenameStemExact)));
                }
                else
                {
                    var gtStemNorm = NormalizeStem(gtStem);
                    var candidateStemNorm = NormalizeStem(candidateStem);

                    if (!string.IsNullOrEmpty(gtStemNorm) &&
                        string.Equals(gtStemNorm, candidateStemNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        facts.Add(new EvidenceFact(nameof(ScoringWeights.FilenameStemNormalized)));
                    }
                }
            }

            // ── Name signals ───────────────────────────────────────────────

            var gtName = gt.Name ?? string.Empty;
            var candidateName = candidate.Name ?? string.Empty;

            if (!string.IsNullOrEmpty(gtName) && !string.IsNullOrEmpty(candidateName))
            {
                if (string.Equals(gtName, candidateName, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new EvidenceFact(nameof(ScoringWeights.NameExact)));
                }
                else
                {
                    var gtNameNorm = NormalizeName(gtName);
                    var candidateNameNorm = NormalizeName(candidateName);

                    if (!string.IsNullOrEmpty(gtNameNorm) &&
                        string.Equals(gtNameNorm, candidateNameNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        facts.Add(new EvidenceFact(nameof(ScoringWeights.NameNormalized)));
                    }
                }
            }

            // ── Folder signals ─────────────────────────────────────────────

            var gtParent = GetFolderName(gt.Path, levels: 1);
            var candidateParent = GetFolderName(candidate.Path, levels: 1);

            if (!string.IsNullOrEmpty(gtParent) && !string.IsNullOrEmpty(candidateParent) &&
                string.Equals(gtParent, candidateParent, StringComparison.OrdinalIgnoreCase))
            {
                facts.Add(new EvidenceFact(nameof(ScoringWeights.ParentFolderMatch)));
            }

            var gtGrandparent = GetFolderName(gt.Path, levels: 2);
            var candidateGrandparent = GetFolderName(candidate.Path, levels: 2);

            if (!string.IsNullOrEmpty(gtGrandparent) && !string.IsNullOrEmpty(candidateGrandparent) &&
                string.Equals(gtGrandparent, candidateGrandparent, StringComparison.OrdinalIgnoreCase))
            {
                facts.Add(new EvidenceFact(nameof(ScoringWeights.GrandparentFolderMatch)));
            }

            return facts;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static string GetStem(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return string.Empty;
            try { return Path.GetFileNameWithoutExtension(fullPath) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string NormalizeStem(string stem)
        {
            if (string.IsNullOrEmpty(stem)) return string.Empty;
            return TrackPrefixRegex.Replace(stem.Trim(), string.Empty).ToLowerInvariant();
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return Regex.Replace(name.Trim().ToLowerInvariant(), @"\s+", " ");
        }

        /// <summary>
        /// Returns the folder name at <paramref name="levels"/> levels up from the file.
        /// levels=1 → parent (album), levels=2 → grandparent (artist / show).
        /// Returns empty string on any error or if the path is too shallow.
        /// Year-bracket prefixes are stripped from the result.
        /// </summary>
        private static string GetFolderName(string fullPath, int levels)
        {
            if (string.IsNullOrEmpty(fullPath)) return string.Empty;
            try
            {
                var dir = Path.GetDirectoryName(fullPath);
                for (var i = 1; i < levels; i++)
                {
                    if (string.IsNullOrEmpty(dir)) return string.Empty;
                    dir = Path.GetDirectoryName(dir);
                }
                if (string.IsNullOrEmpty(dir)) return string.Empty;
                var name = Path.GetFileName(dir) ?? string.Empty;
                return YearFolderRegex.Replace(name.Trim(), string.Empty).Trim();
            }
            catch { return string.Empty; }
        }
    }
}