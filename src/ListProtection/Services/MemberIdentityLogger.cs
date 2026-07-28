using ListProtection.Storage;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ListProtection.Services
{
    /// <summary>
    /// DIAGNOSTIC ONLY. Logs, per GroundTruthMember, which identity/evidence fields are
    /// actually populated for that item's type — so when testing a new member type
    /// (Artist, BoxSet-as-member, etc.) the logs show exactly what the plugin has to
    /// hang scoring on, rather than needing to infer it from repair behaviour.
    ///
    /// Field list is intentionally generic across all types — it does not attempt to
    /// judge whether a field "should" be populated for a given MediaType, only whether
    /// it is, for this specific item, at this specific moment.
    /// </summary>
    public static class MemberIdentityLogger
    {
        private static readonly (string Name, Func<GroundTruthMember, bool> IsPopulated)[] Fields =
        {
            ("Path",               m => !string.IsNullOrEmpty(m.Path)),
            ("RunTimeTicks",       m => m.RunTimeTicks.HasValue),
            ("ProductionYear",     m => m.ProductionYear.HasValue),
            ("Album",              m => !string.IsNullOrEmpty(m.Album)),
            ("AlbumArtist",        m => !string.IsNullOrEmpty(m.AlbumArtist)),
            ("Artists",            m => m.Artists != null && m.Artists.Count > 0),
            ("IndexNumber",        m => m.IndexNumber.HasValue),
            ("MusicBrainzTrackId", m => !string.IsNullOrEmpty(m.MusicBrainzTrackId)),
            ("SeriesName",         m => !string.IsNullOrEmpty(m.SeriesName)),
            ("SeasonNumber",       m => m.SeasonNumber.HasValue),
            ("SeriesTvdbId",       m => !string.IsNullOrEmpty(m.SeriesTvdbId)),
            ("SeriesImdbId",       m => !string.IsNullOrEmpty(m.SeriesImdbId)),
            ("ImdbId",             m => !string.IsNullOrEmpty(m.ImdbId)),
            ("TmdbId",             m => !string.IsNullOrEmpty(m.TmdbId)),
        };

        /// <summary>
        /// Logs a three-line block for one member: identity header, populated fields,
        /// empty fields. <paramref name="tag"/> is a caller-supplied prefix (e.g.
        /// "[DiagnosticDumpTask][MemberIdentity][Playlist]") so log lines are
        /// greppable per call site.
        /// </summary>
        public static void LogIdentity(GroundTruthMember member, ILogger logger, string tag)
        {
            if (member == null || logger == null) return;

            var populated = new List<string>();
            var empty = new List<string>();

            foreach (var field in Fields)
            {
                if (field.IsPopulated(member))
                    populated.Add(field.Name);
                else
                    empty.Add(field.Name);
            }

            logger.Info(
                "{0} Name='{1}' Type={2} InternalId={3}",
                tag, member.Name ?? "(unnamed)", member.MediaType ?? "(unknown)", member.InternalId);
            logger.Info(
                "{0}   Populated: {1}",
                tag, populated.Count > 0 ? string.Join(", ", populated) : "(none)");
            logger.Info(
                "{0}   Empty:     {1}",
                tag, empty.Count > 0 ? string.Join(", ", empty) : "(none)");
        }
    }
}