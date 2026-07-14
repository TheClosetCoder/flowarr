using Flowarr.Core.Entities;
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
        var run = await jobRunService.StartAsync("cleanup.generate", cancellationToken);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedDbContext = scope.ServiceProvider.GetRequiredService<FlowarrDbContext>();
            var scopedJobRunService = scope.ServiceProvider.GetRequiredService<IJobRunService>();
            var scopedCleanupService = scope.ServiceProvider.GetRequiredService<ICleanupRecommendationService>();

            var trackedRun = await scopedDbContext.JobRuns.FindAsync([run.Id], CancellationToken.None);
            if (trackedRun is null)
            {
                return;
            }

            try
            {
                await scopedCleanupService.GenerateCandidatesAsync(
                    onProgress: message => scopedJobRunService.ReportProgressAsync(trackedRun, message, CancellationToken.None),
                    cancellationToken: CancellationToken.None);
                await scopedJobRunService.CompleteAsync(trackedRun, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cleanup candidate generation failed");
                await scopedJobRunService.FailAsync(trackedRun, ex, CancellationToken.None);
            }
        });

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
}

public sealed record ApproveCleanupRequest(string? Actor, bool DryRun = true, string? ConfirmPath = null);
public sealed record RejectCleanupRequest(string? Actor);
