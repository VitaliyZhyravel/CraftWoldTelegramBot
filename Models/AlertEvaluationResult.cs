namespace WebApplication1.Models;

public sealed class AlertEvaluationResult
{
    public required bool ShouldTriggerAlert { get; init; }

    public required bool ShouldUpdateBasePrice { get; init; }

    public required decimal ChangePercent { get; init; }
}
