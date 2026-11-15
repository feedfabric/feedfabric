package feedfabric

import (
	"encoding/json"
	"fmt"
	"net/http"
	"strconv"
	"time"
)

// Error is an API error. Every one has a stable Code (docs/errors.md): match on Code, not on Message.
//
//	var ffErr *feedfabric.Error
//	if errors.As(err, &ffErr) && ffErr.Code == "post.not_found" { ... }
type Error struct {
	// The HTTP status; 0 when the request never got a response
	Status int
	// e.g. "post.not_found", "content.violation", "rate_limited"; "network_error" without a response
	Code    string
	Message string
	Params  map[string]any
	// validation_failed lists fields; post.data_invalid lists {"path", "message"}
	Errors  []json.RawMessage
	TraceID string
	// From Retry-After on 429 and 503
	RetryAfter    time.Duration
	hasRetryAfter bool
}

func (e *Error) Error() string {
	return fmt.Sprintf("feedfabric: %d %s: %s", e.Status, e.Code, e.Message)
}

func errorFrom(response *http.Response, body []byte) *Error {
	var problem struct {
		Code    string            `json:"code"`
		Detail  string            `json:"detail"`
		Params  map[string]any    `json:"params"`
		Errors  []json.RawMessage `json:"errors"`
		TraceID string            `json:"traceId"`
	}
	// Not Problem Details when a proxy or load balancer answered
	_ = json.Unmarshal(body, &problem)
	e := &Error{Status: response.StatusCode, Code: problem.Code, Message: problem.Detail, Params: problem.Params, Errors: problem.Errors, TraceID: problem.TraceID}
	if e.Code == "" {
		e.Code = "http_" + strconv.Itoa(response.StatusCode)
	}
	if e.Message == "" {
		e.Message = response.Status
	}
	if e.Params == nil {
		e.Params = map[string]any{}
	}
	if seconds, err := strconv.ParseFloat(response.Header.Get("Retry-After"), 64); err == nil {
		e.RetryAfter, e.hasRetryAfter = time.Duration(seconds*float64(time.Second)), true
	}
	return e
}
