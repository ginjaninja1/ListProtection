using ListProtection.Storage;
using MediaBrowser.Controller.Entities;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Determines whether a candidate is eligible for automatic repair of a
    /// specific missing member, without user involvement.
    ///
    /// One implementation per media type:
    ///   AudioAutoRepairEligibility — three-field hard gate (name + artist + album)
    ///   Future: EpisodeAutoRepairEligibility, MovieAutoRepairEligibility, etc.
    ///
    /// This is intentionally separate from scoring. The score ranks candidates
    /// for human review. Eligibility gates what the machine may repair silently.
    ///
    /// Implementations must never throw — return false on any error.
    /// </summary>
    public interface IAutoRepairEligibility
    {
        string MediaType { get; }

        bool IsEligible(GroundTruthMember missingMember, BaseItem candidate);
    }
}