namespace PlaylistProtection.Core.Models
{
    public class RuleResult
    {
        public int Score { get; }
        public string Reason { get; }

        public bool HardReject { get; }

        public RuleResult(int score, string reason, bool hardReject = false)
        {
            Score = score;
            Reason = reason;
            HardReject = hardReject;
        }
    }
}