using PlaylistProtection.Core.Models;

namespace PlaylistProtection.Core.Confidence
{
    public interface IConfidenceRule
    {
        string Name { get; }

        int Evaluate(MissingItem missing, CandidateItem candidate);
    }
}