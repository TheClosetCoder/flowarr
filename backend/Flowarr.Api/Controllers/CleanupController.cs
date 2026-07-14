using Flowarr.Core.Entities;
using Flowarr.Core.Enums;
using Flowarr.Core.Services;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/cleanup")]
public sealed class CleanupController(
    FlowarrDbContext dbContext,
    ICleanupRecommendationService cleanupRecommendationService,
    IConnectionSettingsService connectionSettingsService,
    IJobRunService jobRunService,
    IServiceScopeFactory scopeFactory,
    ILogger<CleanupController> logger) : ControllerBase
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
    public async Task<IActionResult> GetCandidates([FromQuery] CleanupStatus? status, CancellationToken cancellationToken)
    {
        var query = dbContext.CleanupCandidates.AsQueryable();
        if (status is { } value)
        {
            query = query.Where(candidate => candidate.Status == value);
        }

        var candidates = await query
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(candidates);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(CancellationToken cancellationToken)
    {
        var run = await jobRunService.StartAsync("cleanup.generate", cancellationToken);

        _ = Task.Run(() => CleanupGenerationRunner.RunTrackedAsync(scopeFactory, run.Id, logger, CancellationToken.None));

        return Accepted(new { jobId = run.Id });
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

    [HttpPost("candidates/bulk-approve")]
    public async Task<IActionResult> BulkApprove(BulkApproveCleanupRequest request, CancellationToken cancellationToken)
    {
        if (request.CandidateIds is not { Length: > 0 })
        {
            return BadRequest(new { message = "No candidates specified." });
        }

        try
        {
            var results = await cleanupRecommendationService.BulkApproveAsync(
                request.CandidateIds,
                string.IsNullOrWhiteSpace(request.Actor) ? "local-user" : request.Actor,
                request.DryRun,
                cancellationToken);

            return Ok(results);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("candidates/bulk-reject")]
    public async Task<IActionResult> BulkReject(BulkRejectCleanupRequest request, CancellationToken cancellationToken)
    {
        if (request.CandidateIds is not { Length: > 0 })
        {
            return BadRequest(new { message = "No candidates specified." });
        }

        var results = await cleanupRecommendationService.BulkRejectAsync(
            request.CandidateIds,
            string.IsNullOrWhiteSpace(request.Actor) ? "local-user" : request.Actor,
            cancellationToken);

        return Ok(results);
    }
}

public sealed record ApproveCleanupRequest(string? Actor, bool DryRun = true, string? ConfirmPath = null);
public sealed record RejectCleanupRequest(string? Actor);
public sealed record BulkApproveCleanupRequest(string? Actor, bool DryRun, Guid[] CandidateIds);
public sealed record BulkRejectCleanupRequest(string? Actor, Guid[] CandidateIds);
