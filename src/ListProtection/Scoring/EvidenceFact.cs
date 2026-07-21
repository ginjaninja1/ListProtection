namespace ListProtection.Scoring
{
    /// <summary>
    /// A single named boolean signal produced by an IEvidenceCollector.
    ///
    /// A fact is either present (fired) or absent — the scorer applies the
    /// configured weight for the signal name when a fact is present.
    ///
    /// SignalName must match a key in ScoringWeights to contribute to the score.
    /// Facts with no matching weight are recorded in MatchedSignals but score 0.
    /// </summary>
    public sealed class EvidenceFact
    {
        /// <summary>
        /// Identifies the signal — must match a key in ScoringWeights.
        /// e.g. "FilenameStemExact", "AlbumExact", "NameNormalized"
        /// </summary>
        public string SignalName { get; }

        public EvidenceFact(string signalName)
        {
            SignalName = signalName;
        }
    }
}