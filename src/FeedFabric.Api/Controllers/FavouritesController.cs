using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Favourites;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

[ApiController]
[Authorize]
[Route("favourites")]
public class FavouritesController(IFavouriteService favourites, ICurrentUser currentUser) : ControllerBase
{
    // GET /favourites: the accounts you've marked as favourites, most recently added first
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        await favourites.ListAsync(currentUser.ExternalUserId, cancellationToken) is { } list ? Ok(list) : ToProblem(FavouriteResultStatus.UserNotFound, "");

    // PUT /favourites/{externalUserId}; repeating it is a no-op. At most 30.
    [HttpPut("{externalUserId}")]
    public async Task<IActionResult> Add(string externalUserId, CancellationToken cancellationToken)
    {
        var status = await favourites.AddAsync(currentUser.ExternalUserId, externalUserId, cancellationToken);
        return status == FavouriteResultStatus.Ok ? NoContent() : ToProblem(status, externalUserId);
    }

    // DELETE /favourites/{externalUserId}; repeating it is a no-op
    [HttpDelete("{externalUserId}")]
    public async Task<IActionResult> Remove(string externalUserId, CancellationToken cancellationToken)
    {
        var status = await favourites.RemoveAsync(currentUser.ExternalUserId, externalUserId, cancellationToken);
        return status == FavouriteResultStatus.Ok ? NoContent() : ToProblem(status, externalUserId);
    }

    private ObjectResult ToProblem(FavouriteResultStatus status, string targetExternalUserId) => status switch
    {
        FavouriteResultStatus.UserNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotSynced,
            $"User {currentUser.ExternalUserId} has not been synced.", new { externalUserId = currentUser.ExternalUserId }),
        FavouriteResultStatus.TargetNotFound => this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotFound,
            $"User {targetExternalUserId} not found.", new { externalUserId = targetExternalUserId }),
        FavouriteResultStatus.CannotFavouriteSelf => this.ApiProblem(StatusCodes.Status400BadRequest, ErrorCodes.FavouriteSelf,
            "Users can't make themselves a favourite."),
        FavouriteResultStatus.LimitReached => this.ApiProblem(StatusCodes.Status409Conflict, ErrorCodes.FavouriteLimitReached,
            $"You can have up to {FavouriteService.MaxFavourites} favourites. Remove one to add another.", new { max = FavouriteService.MaxFavourites }),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
