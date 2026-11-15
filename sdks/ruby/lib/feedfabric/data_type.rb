# frozen_string_literal: true

module FeedFabric
  # One of your data types, typed. The API checks data against the type's JSON Schema; this makes the same promise in
  # your code, so a weight reading can't be posted as anything else and reading one back gives your own type.
  #
  #   WeightReading = Data.define(:kg, :note)
  #   WEIGHT = FeedFabric::DataType.new("weight-reading",
  #                                     parse: ->(v) { WeightReading.new(kg: v.fetch("kg"), note: v["note"]) },
  #                                     dump: ->(r) { r.to_h.compact })
  #
  #   member.posts.create(tags: ["weight"], data: WEIGHT.of(WeightReading.new(kg: 82.4, note: nil)))
  #   WEIGHT.read(post) # => #<data WeightReading kg=82.4, note=nil>, or nil
  #
  # Without parse and dump, values are the decoded JSON itself (a Hash with string keys). sig/feedfabric.rbs types
  # it as DataType[T], for Steep.
  class DataType
    attr_reader :key

    # parse turns the decoded JSON into your type (raise if it isn't one); dump turns yours into something JSON writes
    def initialize(key, parse: nil, dump: nil)
      @key = key
      @parse = parse
      @dump = dump
    end

    # The post's data, for posts.create and posts.edit
    def of(value)
      { "type" => key, "value" => @dump ? @dump.call(value) : value }
    end

    # Whether a post (anything with its "data") has data of this type
    def is?(post)
      data = post["data"]
      data.is_a?(Hash) && data["type"] == key
    end

    # The post's data as your type, or nil when it has none of this type
    def read(post)
      return nil unless is?(post)

      value = post["data"]["value"]
      @parse ? @parse.call(value) : value
    end
  end
end
