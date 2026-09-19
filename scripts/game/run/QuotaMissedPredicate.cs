namespace Sail.Game.Run;

/// <summary>
/// The ONE shipped loss predicate (core-spine spec §1.6; canon fact 15 as amended
/// 2026-08-13: the missed quota is the only ending — no win line, no other loss). Evaluated
/// by <see cref="PlaythroughMachine"/> at the verdict instant only. No second predicate is
/// invented in this packet — per the breakdown's gate record there is no incapacitation-loss
/// slot to keep warm (instant respawn at hearths means a group wipe needs no scripted rule);
/// the chain shape exists so a future predicate registers without rework.
/// </summary>
public sealed class QuotaMissedPredicate : ILossPredicate
{
    private readonly QuotaLedger _ledger;

    public QuotaMissedPredicate(QuotaLedger ledger) => _ledger = ledger;

    public string Id => "quota-missed";

    public RunOutcome? Evaluate(int round)
    {
        if (!_ledger.IsQuotaMissed())
            return null;
        // Report the numbers the verdict actually compared (spec §1.6 — "for the loss
        // screen's honesty"), which depend on the carry model.
        return _ledger.CarrySurplus
            ? new RunOutcome(RunOutcomeKind.QuotaMissed, round, _ledger.CumulativeDemand, _ledger.CumulativeBanked)
            : new RunOutcome(RunOutcomeKind.QuotaMissed, round, _ledger.DemandCurrentRound, _ledger.BankedThisRound);
    }
}
