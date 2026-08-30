using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversitySchedule.Application.Identity;
using UniversitySchedule.Contracts.Identity;

namespace UniversitySchedule.Api.Controllers;

[ApiController]
[Route("api/v1/installations")]
public sealed class InstallationsController(
    InstallationRegistrationService registrationService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType<RegisterInstallationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RegisterInstallationResponse>> Register(
        RegisterInstallationRequest request,
        CancellationToken cancellationToken)
    {
        InstallationRegistrationResult result = await registrationService.RegisterAsync(
            new InstallationRegistrationCommand(
                request.InstallationId,
                request.InstallationSecret,
                request.Platform,
                request.AppVersion),
            cancellationToken);

        return result.Status switch
        {
            InstallationRegistrationStatus.Success => Ok(new RegisterInstallationResponse(
                result.InstallationId,
                result.AccessToken!.Value,
                "Bearer",
                result.AccessToken.ExpiresAtUtc,
                result.IsNewInstallation)),
            InstallationRegistrationStatus.InvalidRequest => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Некорректные данные установки",
                result.ErrorCode!)),
            InstallationRegistrationStatus.InvalidCredentials => Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Не удалось подтвердить установку",
                result.ErrorCode!)),
            InstallationRegistrationStatus.Revoked => StatusCode(
                StatusCodes.Status403Forbidden,
                CreateProblem(
                    StatusCodes.Status403Forbidden,
                    "Установка отозвана",
                    result.ErrorCode!)),
            _ => throw new InvalidOperationException($"Unsupported registration status: {result.Status}."),
        };
    }

    private static ProblemDetails CreateProblem(int status, string title, string code)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
        };
        problem.Extensions["code"] = code;
        return problem;
    }
}
