namespace WebApplication1.Models;

public sealed class AlertEvaluationResult
{
    public required bool ShouldTrigger { get; init; }

    public required decimal ChangePercent { get; init; }
}
