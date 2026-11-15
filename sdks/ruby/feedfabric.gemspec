# frozen_string_literal: true

require_relative "lib/feedfabric/version"

Gem::Specification.new do |spec|
  spec.name = "feedfabric"
  spec.version = FeedFabric::VERSION
  spec.summary = "Call FeedFabric from your servers, for your members, with an API key."
  spec.authors = ["FeedFabric"]
  spec.license = "MIT"
  spec.required_ruby_version = ">= 3.1"
  spec.files = Dir["lib/**/*.rb", "sig/**/*.rbs", "README.md"]
  spec.require_paths = ["lib"]
  spec.metadata["rubygems_mfa_required"] = "true"
end
