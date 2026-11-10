// Every API error is Problem Details with a stable code (docs/errors.md). Match on code, not on message.
export interface FieldError {
  field?: string
  code: string
  params?: Record<string, unknown>
}

export class FeedFabricError extends Error {
  override readonly name = 'FeedFabricError'

  constructor(
    // The HTTP status; 0 when the request never got a response
    readonly status: number,
    // e.g. "post.not_found", "content.violation", "rate_limited"; "network_error" without a response
    readonly code: string,
    message: string,
    readonly params: Record<string, unknown> = {},
    // validation_failed lists fields; post.data_invalid lists { path, message }
    readonly errors: unknown[] = [],
    readonly traceId?: string,
    // Seconds to wait, from Retry-After on 429 and 503
    readonly retryAfter?: number,
  ) {
    super(message)
  }

  static async from(response: Response): Promise<FeedFabricError> {
    let body: Record<string, unknown> = {}
    try {
      body = (await response.json()) as Record<string, unknown>
    } catch {
      // Not Problem Details: a proxy or load balancer answered
    }
    const retryAfter = Number(response.headers.get('retry-after') ?? NaN)
    return new FeedFabricError(
      response.status,
      typeof body.code === 'string' ? body.code : `http_${response.status}`,
      typeof body.detail === 'string' ? body.detail : `${response.status} ${response.statusText}`,
      (body.params as Record<string, unknown> | undefined) ?? {},
      (body.errors as unknown[] | undefined) ?? [],
      typeof body.traceId === 'string' ? body.traceId : undefined,
      Number.isFinite(retryAfter) ? retryAfter : undefined,
    )
  }
}
