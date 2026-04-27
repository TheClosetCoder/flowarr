using Flowarr.Core.Services;
using Flowarr.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/cleanup")]
public sealed class CleanupController(
    FlowarrDbContext dbContext,
    ICleanupRecommendationService cleanupRecommendationService) : ControllerBase
{
    [HttpGet("candidates")]
    public async Task<IActionResult> GetCandidates(CancellationToken cancellationToken)
    {
        var candidates = await dbContext.CleanupCandidates
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(candidates);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(CancellationToken cancellationToken)
    {
        var candidates = await cleanupRecommendationService.GenerateCandidatesAsync(cancellationToken);
        return Ok(candidates);
    }

    [HttpPost("candidates/{candidateId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid candidateId, ApproveCleanupRequest request, CancellationToken cancellationToken)
    {
        var candidate = await cleanupRecommendationService.ApproveAsync(
            candidateId,
            string.IsNullOrWhiteSpace(request.Actor) ? "local-user" : request.Actor,
            request.DryRun,
            cancellationToken);

        return candidate is null ? NotFound() : Ok(candidate);
    }

    [HttpPost("candidates/{candidateId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid candidateId, RejectCleanupRequest request, CancellationToken cancellationToken)
    {
        var candidate = await cleanupRecommendationService.RejectAsync(
            candidateId,
            string.IsNullOrWhiteSpace(request.Actor) ? "local-user" : request.Actor,
            cancellationToken);

        return candidate is null ? NotFound() : Ok(candidate);
    }
}

public sealed record ApproveCleanupRequest(string? Actor, bool DryRun = true);
public sealed record RejectCleanupRequest(string? Actor);
