using ListProtection.Configuration;
using ListProtection.Scoring;
using ListProtection.UIBaseClasses.Views;
using MediaBrowser.Model.Plugins.UI.Views;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ListProtection.UI.Config
{
    internal sealed class ScoringReferenceDialog : PluginDialogView
    {
        public ScoringReferenceDialog(string pluginId, PluginConfiguration config) : base(pluginId)
        {
            ShowDialogFullScreen = true;
            AllowOk = false;
            AllowCancel = true;
            ContentData = Build(config);
        }

        public override string Caption => "Scoring Reference";
        public override bool ShowDialogFullScreen { get; }

        public override Task OnCancelCommand() => Task.CompletedTask;

        public override Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
            => base.RunCommand(itemId, commandId, data);

        private static ScoringReferenceDialogUI Build(PluginConfiguration config)
        {
            var rows = new List<ScoringReferenceRow>();

            // ── Tier 1 — Content (media-type specific, best match wins) ───
            AddContentRules(rows, "Content — Audio", ScoringWeights.AudioRules, AudioContentNotes);
            AddContentRules(rows, "Content — Episode", ScoringWeights.EpisodeRules, EpisodeContentNotes);
            AddContentRules(rows, "Content — Movie", ScoringWeights.MovieRules, MovieContentNotes);

            // ── Tier 3 — Fallback (media-type agnostic, appears once) ─────
            AddFallbackRules(rows);

            // ── Tier 2 — Location (media-type agnostic, appears once) ─────
            AddFolderRules(rows);

            return ScoringReferenceDialogUI.Build(
                rows.ToArray(),
                config.AutoRepairScoreThreshold,
                config.AutoRepairMinCandidateDistance,
                config.ManualRepairScoreThreshold,
                config.ManualRepairMinCandidateDistance);
        }

        // ── Tier 1 helpers ─────────────────────────────────────────────────

        private static void AddContentRules(
            List<ScoringReferenceRow> rows,
            string tier,
            List<ScoringRule> rules,
            IReadOnlyDictionary<string, string> notes)
        {
            foreach (var rule in rules)
            {
                var signal = string.Join(" + ", rule.RequiredFacts);
                notes.TryGetValue(signal, out var note);
                rows.Add(new ScoringReferenceRow
                {
                    Tier = tier,
                    Score = rule.Score,
                    Signal = signal,
                    Notes = note ?? string.Empty
                });
            }
        }

        // Per-rule notes keyed by signal string (joined facts).
        // Only rules that warrant elaboration need an entry.

        private static readonly Dictionary<string, string> AudioContentNotes =
            new Dictionary<string, string>
            {
                [AudioFacts.MbTrackIdMatch] =
                    "Definitive — MusicBrainz Track ID exact match. Short-circuit: no further " +
                    "facts are collected if this fires.",

                [AudioFacts.NameMatch + " + " + AudioFacts.ArtistMatch + " + " +
                 AudioFacts.AlbumMatch + " + " + AudioFacts.TrackNumberMatch] =
                    "Four-field anchor. TrackNumber only fires when IndexNumber > 0 on both " +
                    "the GT snapshot and the candidate.",

                [AudioFacts.ArtistMatch + " + " + AudioFacts.AlbumMatch] =
                    "Weakest content rule — matches every track on the same album by the same " +
                    "artist. Score 25 sibling-album noise is expected and normal; threshold " +
                    "gates suppress it. ArtistMatch compares GT primary artist (Artists[0]) only.",
            };

        private static readonly Dictionary<string, string> EpisodeContentNotes =
            new Dictionary<string, string>
            {
                [EpisodeFacts.SeriesMatch] =
                    "SeriesMatch is a prerequisite gate: if neither the series name nor a " +
                    "series provider ID (TVDB / IMDB) matches, no facts at all are emitted for " +
                    "that candidate — it is excluded from scoring entirely.",

                [EpisodeFacts.SeriesMatch + " + " + EpisodeFacts.SeasonMatch + " + " +
                 EpisodeFacts.EpisodeMatch + " + " + EpisodeFacts.TitleMatch] =
                    "Four-field anchor — series, season, episode number, and title all match.",

                [EpisodeFacts.SeriesMatch + " + " + EpisodeFacts.SeasonMatch + " + " +
                 EpisodeFacts.EpisodeMatch + " + " + EpisodeFacts.DurationMatch] =
                    "Series + position + duration — strong when episode titles differ across " +
                    "sources or are absent.",
            };

        private static readonly Dictionary<string, string> MovieContentNotes =
            new Dictionary<string, string>
            {
                [MovieFacts.ImdbIdMatch] =
                    "Definitive — IMDB ID exact match. Short-circuit: no further facts collected.",

                [MovieFacts.TmdbIdMatch] =
                    "Definitive — TMDB ID exact match. Short-circuit: no further facts collected. " +
                    "IMDB checked first; TMDB only evaluated if IMDB did not match.",

                [MovieFacts.DurationMatch] =
                    "Duration alone is weak for movies — many films share a similar runtime. " +
                    "Useful as corroboration only.",
            };

        // ── Tier 3 — Fallback ──────────────────────────────────────────────

        private static void AddFallbackRules(List<ScoringReferenceRow> rows)
        {
            var fallbackNotes = new Dictionary<string, string>
            {
                [FallbackFacts.NameExact + " + " + FallbackFacts.FilenameStemExact] =
                    "Strongest fallback — display name exact match AND filename stem exact match. " +
                    "Fires only when Tier 1 (Content) score is zero.",

                [FallbackFacts.NameNormalized] =
                    "Display name after whitespace collapse and case fold. " +
                    "Name and filename are independent fields and can stack.",

                [FallbackFacts.FilenameStemNorm] =
                    "Filename stem after stripping a leading numeric track prefix " +
                    "(e.g. '01 - ' or '02. ') and case fold.",
            };

            foreach (var rule in ScoringWeights.FallbackRules)
            {
                var signal = string.Join(" + ", rule.RequiredFacts);
                fallbackNotes.TryGetValue(signal, out var note);
                rows.Add(new ScoringReferenceRow
                {
                    Tier = "Fallback — all media types (Tier 1 score = 0 only)",
                    Score = rule.Score,
                    Signal = signal,
                    Notes = note ?? "Fires only when Tier 1 (Content) score is zero."
                });
            }
        }

        // ── Tier 2 — Location ──────────────────────────────────────────────

        private static void AddFolderRules(List<ScoringReferenceRow> rows)
        {
            for (var depth = 1; depth <= FolderEvidenceCollector.MaxDepth; depth++)
            {
                var marginal = FolderFacts.WeightForDepth(depth);
                var cumulative = FolderFacts.CumulativeWeightForDepth(depth);

                string note;
                if (depth == 1)
                    note = "Immediate parent folder name matches. Marginal contribution only — " +
                           "meaningful as corroboration alongside other signals.";
                else if (depth <= 5)
                    note = depth + " consecutive ancestor names match (immediate parent upward). " +
                           "Cumulative location score at this depth: " + cumulative + ". " +
                           "Chain breaks at first mismatch — no deeper facts fire.";
                else
                    note = depth + " consecutive ancestor names match. Cumulative: " + cumulative + ". " +
                           "Diminishing marginal return beyond depth 5 — primarily disambiguation.";

                rows.Add(new ScoringReferenceRow
                {
                    Tier = "Location — all media types (requires content anchor)",
                    Score = marginal,
                    Signal = FolderFacts.Depth(depth),
                    Notes = note
                });
            }
        }
    }
}