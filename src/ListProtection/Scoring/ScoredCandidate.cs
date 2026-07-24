using ListProtection.Storage;
using MediaBrowser.Controller.Entities;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Pairs a CandidateEntry (store record) with its resolved live BaseItem
    /// for use in eligibility gate evaluation.
    ///
    /// AutoRepairer builds these after resolving candidate InternalIds against
    /// the library. Entries where the live item could not be resolved are excluded.
    /// </summary>
    public sealed class ScoredCandidate
    {
        public CandidateEntry Entry { get; }
        public BaseItem LiveItem { get; }

        public int Score => Entry.Score;

        public ScoredCandidate(CandidateEntry entry, BaseItem liveItem)
        {
            Entry = entry;
            LiveItem = liveItem;
        }
    }
}