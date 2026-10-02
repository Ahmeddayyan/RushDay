using RushDay.Infrastructure.Ops;

namespace RushDay.Api.Contracts;

/// <summary>A module whose <c>enrolled_count</c> the reconciliation corrected.</summary>
public sealed record ModuleCorrectionView(string Code, int Before, int After);

/// <summary><c>POST /api/admin/ops/reconcile</c>.</summary>
public sealed record ReconcileResponse(IReadOnlyList<ModuleCorrectionView> ModulesCorrected)
{
    public static ReconcileResponse From(IReadOnlyList<ModuleCorrection> corrections)
    {
        ArgumentNullException.ThrowIfNull(corrections);
        return new ReconcileResponse([.. corrections.Select(c => new ModuleCorrectionView(c.Code, c.Before, c.After))]);
    }
}

/// <summary><c>POST /api/admin/ops/demo-reset</c> (demo mode only).</summary>
public sealed record DemoResetResponse(int Withdrawn);
