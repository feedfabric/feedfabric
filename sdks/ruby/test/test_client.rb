# frozen_string_literal: true

require "minitest/autorun"
require "socket"
require "feedfabric"

# A stand-in API: records each request and answers with whatever the test queued
class FakeApi
  attr_reader :seen, :url

  def initialize
    @seen = []
    @replies = Queue.new
    @server = TCPServer.new("127.0.0.1", 0)
    @url = "http://127.0.0.1:#{@server.addr[1]}"
    @thread = Thread.new { loop { handle(@server.accept) } }
  end

  def reply(status, body = nil, headers = {})
    @replies << [status, body, headers]
  end

  def close
    @thread.kill
    @server.close
  end

  private

  def handle(socket)
    method, path = socket.gets.split
    headers = {}
    while (line = socket.gets) && line != "\r\n"
      name, value = line.split(":", 2)
      headers[name.downcase] = value.strip
    end
    body = socket.read(headers.fetch("content-length", "0").to_i).to_s
    @seen << { method: method, path: path, headers: headers, body: body }
    status, reply, extra = @replies.empty? ? [204, nil, {}] : @replies.pop
    payload = reply.nil? ? "" : JSON.generate(reply)
    socket.write("HTTP/1.1 #{status} X\r\nContent-Type: application/json\r\nContent-Length: #{payload.bytesize}\r\nConnection: close\r\n")
    extra.each { |name, value| socket.write("#{name}: #{value}\r\n") }
    socket.write("\r\n#{payload}")
  ensure
    socket.close
  end
end

WeightReading = Data.define(:kg, :note)

class ClientTest < Minitest::Test
  WEIGHT = FeedFabric::DataType.new("weight-reading",
                                    parse: ->(v) { WeightReading.new(kg: Float(v.fetch("kg")), note: v["note"]) },
                                    dump: ->(r) { r.to_h.compact })

  SAVED = {
    "id" => "p1", "authorId" => "a1", "content" => "", "createdAt" => "2026-11-10T09:00:00Z", "editedAt" => nil,
    "repostOfId" => nil, "isHeld" => false, "data" => { "type" => "weight-reading", "version" => 2, "value" => { "kg" => 82.4 } },
  }.freeze

  def setup
    @api = FakeApi.new
  end

  def teardown
    @api.close
  end

  def member
    FeedFabric::Client.new(base_url: @api.url, api_key: "ffk_test").as_member("u1")
  end

  def test_acts_for_the_member_with_the_key_and_encoded_profile
    @api.reply(200, { externalUserId: "user 1" })

    me = FeedFabric::Client.new(base_url: "#{@api.url}/", api_key: "ffk_test").as_member("user 1", display_name: "Zoë Brontë").me

    assert_equal "user 1", me["externalUserId"]
    seen = @api.seen[0]
    assert_equal "GET /me", "#{seen[:method]} #{seen[:path]}"
    assert_equal "Bearer ffk_test", seen[:headers]["authorization"]
    assert_equal "user%201", seen[:headers]["feedfabric-user"]
    assert_equal "Zoë Brontë", URI.decode_uri_component(seen[:headers]["feedfabric-display-name"])
    refute seen[:headers].key?("feedfabric-username")
  end

  def test_posts_typed_data_and_app_tags_and_reads_it_back
    @api.reply(201, SAVED)

    post = member.posts.create(tags: ["weight"], data: WEIGHT.of(WeightReading.new(kg: 82.4, note: nil)))

    assert_equal({ "tags" => ["weight"], "data" => { "type" => "weight-reading", "value" => { "kg" => 82.4 } } }, JSON.parse(@api.seen[0][:body]))
    assert_equal WeightReading.new(kg: 82.4, note: nil), WEIGHT.read(post)
    assert_nil FeedFabric::DataType.new("steps").read(post)
    refute WEIGHT.is?({ "data" => nil })
  end

  def test_read_raises_for_data_that_is_not_the_type
    assert_raises(ArgumentError) { WEIGHT.read({ "data" => { "type" => "weight-reading", "value" => { "kg" => "heavy" } } }) }
  end

  def test_builds_query_strings_and_leaves_out_what_is_unset
    @api.reply(200, { posts: [], nextCursor: nil })

    member.posts.list(type: "weight-reading", order: "oldest", limit: 5)

    assert_equal "/userfeed?type=weight-reading&order=oldest&limit=5", @api.seen[0][:path]
  end

  def test_errors_carry_the_code_params_and_field_errors
    @api.reply(400, { code: "post.data_invalid", detail: "nope", params: { type: "weight-reading" },
                      errors: [{ path: "/kg", message: "should be a number" }], traceId: "t-1" })

    error = assert_raises(FeedFabric::Error) { member.posts.create(data: WEIGHT.of(WeightReading.new(kg: 1, note: nil))) }

    assert_equal [400, "post.data_invalid", "t-1"], [error.status, error.code, error.trace_id]
    assert_equal({ "type" => "weight-reading" }, error.params)
    assert_equal "/kg", error.errors[0]["path"]
  end

  def test_waits_out_a_429_even_for_a_post
    @api.reply(429, { code: "rate_limited" }, { "Retry-After" => "0" })
    @api.reply(202, { counted: 2 })

    assert_equal 2, member.signals.impressions(%w[p1 p2])["counted"]
    assert_equal 2, @api.seen.size
  end

  def test_repeats_a_get_after_a_503_but_not_a_post
    m = member
    @api.reply(503, { code: "service_unavailable" }, { "Retry-After" => "0" })
    assert_raises(FeedFabric::Error) { m.posts.create(content: "hi") }
    assert_equal 1, @api.seen.size

    @api.reply(503, { code: "service_unavailable" }, { "Retry-After" => "0" })
    @api.reply(200, [])
    assert_equal [], m.favourites
    assert_equal 3, @api.seen.size
  end

  def test_uploads_an_image_and_waits_until_it_is_ready
    @api.reply(201, { mediaId: "m1", uploadUrl: "#{@api.url}/blobs/m1", contentType: "image/png", expiresAt: "2026-11-10T10:00:00Z" })
    @api.reply(200)
    @api.reply(202, { id: "m1", status: "processing" })
    @api.reply(200, { id: "m1", status: "ready", url: "u", thumbnailUrl: "t", width: 1, height: 1 })
    client = FeedFabric::Client.new(base_url: @api.url, api_key: "ffk_test")
    client.upload_poll = 0.01

    media = client.as_member("u1").media.upload("\x01\x02\x03".b, "image/png")

    assert_equal "ready", media["status"]
    assert_equal ["POST /media/uploads", "PUT /blobs/m1", "POST /media/m1/complete", "GET /media/m1"],
                 @api.seen.map { |s| "#{s[:method]} #{s[:path]}" }
    refute @api.seen[1][:headers].key?("authorization")
  end

  def test_refuses_a_wrong_looking_key
    assert_raises(ArgumentError) { FeedFabric::Client.new(base_url: "http://localhost", api_key: "eyJhbGciOi...") }
  end
end
