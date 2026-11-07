namespace FeedFabric.Application.Services.Storage;

// A model isn't in blob storage (yet): publish it (make models, or the deploy's model step) and the next try works.
// Callers retry rather than give up, and the readiness check reports it.
public sealed class ModelNotFoundException(string message) : InvalidOperationException(message);
