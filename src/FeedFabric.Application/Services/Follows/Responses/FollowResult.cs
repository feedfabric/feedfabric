namespace FeedFabric.Application.Services.Follows;

public enum FollowResultStatus
{
    Ok,
    UserNotFound,
    TargetNotFound,
    CannotFollowSelf,
}
