using VoxLink.Api.Models;
using VoxLink.Api.Pdf;

namespace VoxLink.Api.Billing;

public record CallUsageRow(string DestinationNumber, int DurationSeconds);

public record UsageBreakdown(
    decimal LocalMinutes,
    decimal InternationalMinutes,
    int CallCount,
    decimal LocalOverageMinutes,
    decimal AmountDue,
    List<InvoiceLineItem> LineItems);

/// <summary>
/// Shared by the live "usage this cycle" endpoint and both invoice
/// generation paths (auto month-end and manual) so the numbers a company
/// sees before billing always match what actually lands on the invoice.
/// </summary>
public static class UsageCalculator
{
    public static UsageBreakdown Compute(IReadOnlyList<CallUsageRow> calls, Plan plan, string localCountryCode)
    {
        var localCalls = calls.Where(c => CallClassifier.IsLocal(c.DestinationNumber, localCountryCode)).ToList();
        var internationalCalls = calls.Except(localCalls).ToList();

        // Each call is rounded up to the minute individually, then summed —
        // not summed in seconds first and rounded once. Two 9-second calls
        // bill as 2 minutes, not 1 (ceil(9/60) + ceil(9/60), not ceil(18/60)).
        var localMinutes = localCalls.Sum(c => Math.Ceiling(c.DurationSeconds / 60m));
        var internationalMinutes = internationalCalls.Sum(c => Math.Ceiling(c.DurationSeconds / 60m));

        // Included minutes only ever pool local usage — international calls
        // are billed from the first minute, never covered by the plan.
        var localOverageMinutes = Math.Max(0, localMinutes - plan.IncludedMinutes);
        var localOverageAmount = localOverageMinutes * plan.LocalRatePerMin;
        var internationalAmount = internationalMinutes * plan.InternationalRatePerMin;
        var amountDue = plan.MonthlyPrice + localOverageAmount + internationalAmount;

        var lineItems = new List<InvoiceLineItem>
        {
            new($"{plan.Name} plan — base fee ({plan.IncludedMinutes} local min included)", plan.MonthlyPrice)
        };
        if (localOverageMinutes > 0)
        {
            lineItems.Add(new($"Local usage overage — {localOverageMinutes} min @ R{plan.LocalRatePerMin:0.00}/min", localOverageAmount));
        }
        if (internationalMinutes > 0)
        {
            lineItems.Add(new($"International usage — {internationalMinutes} min @ R{plan.InternationalRatePerMin:0.00}/min", internationalAmount));
        }

        return new UsageBreakdown(localMinutes, internationalMinutes, calls.Count, localOverageMinutes, amountDue, lineItems);
    }
}
