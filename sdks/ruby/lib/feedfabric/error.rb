# frozen_string_literal: true

require "json"

module FeedFabric
  # An API error. Every one has a stable code (docs/errors.md): match on it, not on the message.
  #
  #   rescue FeedFabric::Error => e
  #     raise unless e.code == "post.not_found"
  class Error < StandardError
    # The HTTP status; 0 when the request never got a response
    attr_reader :status
    # e.g. "post.not_found", "content.violation", "rate_limited"; "network_error" without a response
    attr_reader :code
    attr_reader :params
    # validation_failed lists fields; post.data_invalid lists {"path" => ..., "message" => ...}
    attr_reader :errors
    attr_reader :trace_id
    # Seconds, from Retry-After on 429 and 503
    attr_reader :retry_after

    def initialize(status, code, message, params: {}, errors: [], trace_id: nil, retry_after: nil)
      super(message)
      @status = status
      @code = code
      @params = params
      @errors = errors
      @trace_id = trace_id
      @retry_after = retry_after
    end

    def self.from_response(response)
      status = response.code.to_i
      problem = begin
        JSON.parse(response.body.to_s)
      rescue JSON::ParserError
        # Not Problem Details: a proxy or load balancer answered
        nil
      end
      problem = {} unless problem.is_a?(Hash)
      retry_after = Float(response["Retry-After"], exception: false) if response["Retry-After"]
      new(status, problem["code"] || "http_#{status}", problem["detail"] || "#{status} #{response.message}",
          params: problem["params"].is_a?(Hash) ? problem["params"] : {},
          errors: problem["errors"].is_a?(Array) ? problem["errors"] : [],
          trace_id: problem["traceId"], retry_after: retry_after)
    end
  end
end
