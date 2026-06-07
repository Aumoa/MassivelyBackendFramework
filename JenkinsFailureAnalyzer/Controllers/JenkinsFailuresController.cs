using JenkinsFailureAnalyzer.Abstractions;
using JenkinsFailureAnalyzer.Options;
using JenkinsFailureAnalyzer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JenkinsFailureAnalyzer.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/jenkins/failures")]
public sealed class JenkinsFailuresController(
    IIngestionSecretValidator secretValidator,
    IFailureAnalysisService analysisService,
    IFailureAnalysisStore analysisStore,
    IOptions<FailureAnalysisStorageOptions> storageOptions)
    : ControllerBase
{
    [HttpPost]
    public async ValueTask<ActionResult<JenkinsFailureAnalysisResponse>> AnalyzeAsync(
        [FromBody] JenkinsFailureAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        if (!secretValidator.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Ingestion secret is not configured.");
        }

        if (!Request.Headers.TryGetValue(JenkinsFailureAnalyzerHeaders.Secret, out var secret) ||
            !secretValidator.Validate(secret.ToString()))
        {
            return Unauthorized();
        }

        if (request.Content.Length > storageOptions.Value.MaxContentLength)
        {
            return BadRequest($"Content exceeds the maximum allowed length of {storageOptions.Value.MaxContentLength} characters.");
        }

        var submission = FailureAnalysisSubmission.FromRequest(request);
        var analysis = await analysisService.AnalyzeAsync(submission, cancellationToken);
        var record = await analysisStore.SaveAsync(
            submission,
            analysis,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return new JenkinsFailureAnalysisResponse
        {
            Id = record.Id,
            ReceivedAt = record.ReceivedAt,
            Summary = record.Analysis.Summary,
            LikelyCauses = record.Analysis.LikelyCauses,
            SuggestedActions = record.Analysis.SuggestedActions
        };
    }
}
