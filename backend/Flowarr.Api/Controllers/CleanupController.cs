using Flowarr.Core.Entities;
using Flowarr.Core.Services;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/cleanup")]
public sealed class CleanupController(
    FlowarrDbContext dbContext,
    ICleanupRecommendationService cleanupRecommendationService,
    IConnectionSettingsService connectionSettingsService) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<CleanupSettings>> GetSettings(CancellationToken cancellationToken)
    {
        return await connectionSettingsService.GetCleanupSettingsAsync(cancellationToken);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings(SaveCleanupSettingsRequest request, CancellationToken cancellationToken)
    {
        await connectionSettingsService.SaveCleanupSettingsAsync(request, cancellationToken);
        return NoContent();
    }

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
        CleanupCandidate? candidate;
        try
        {
            candidate = await cleanupRecommendationService.ApproveAsync(
                candidateId,
                string.IsNullOrWhiteSpace(request.Actor) ? "local-user" : request.Actor,
                request.DryRun,
                request.ConfirmPath,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

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

public sealed record ApproveCleanupRequest(string? Actor, bool DryRun = true, string? ConfirmPath = null);
public sealed record RejectCleanupRequest(string? Actor);
