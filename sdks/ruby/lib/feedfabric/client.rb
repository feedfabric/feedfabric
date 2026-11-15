# frozen_string_literal: true

require "erb"
require "json"
require "net/http"
require "uri"

module FeedFabric
  # The SDK's entry point: one per process, shared by every request.
  #
  #   feed = FeedFabric::Client.new(base_url: "https://feed.example.com", api_key: ENV.fetch("FEEDFABRIC_API_KEY"))
  #   member = feed.as_member("user-123", username: "sam")
  #   post = member.posts.create(content: "Hello #intro")
  #
  # Responses are the API's JSON as Hashes with its own (camelCase, string) keys; sig/feedfabric.rbs has their shapes.
  class Client
    # Network errors worth another go, on requests that are safe to repeat
    NETWORK_ERRORS = [IOError, SystemCallError, SocketError, Timeout::Error, OpenSSL::SSL::SSLError].freeze

    # How often uploads check whether the image is ready, in seconds; shortened in tests
    attr_accessor :upload_poll

    # base_url is where the API runs; api_key is a key from the admin panel (Integrations -> API keys), "ffk_...".
    # Requests are retried max_retries times after a 429 or 503 (waiting Retry-After), and after network errors on
    # requests that are safe to repeat.
    def initialize(base_url:, api_key:, timeout: 30, max_retries: 2)
      raise ArgumentError, "FeedFabric needs a base_url" if base_url.to_s.empty?
      raise ArgumentError, 'FeedFabric needs an API key from the admin panel ("ffk_...")' unless api_key.to_s.start_with?("ffk_")

      @base_url = base_url.chomp("/")
      @api_key = api_key
      @timeout = timeout
      @max_retries = max_retries
      @upload_poll = 1.0
    end

    # Calls made for one of your members: their ID is the one your sign-in tokens carry (Auth:Claims:UserId). The
    # profile is used when FeedFabric first sees them, and refreshes theirs at most hourly.
    def as_member(external_user_id, username: nil, display_name: nil, avatar_url: nil)
      raise ArgumentError, "as_member needs the member's ID" if external_user_id.to_s.empty?

      headers = { "FeedFabric-User" => Client.escape(external_user_id) }
      { "FeedFabric-Username" => username, "FeedFabric-Display-Name" => display_name, "FeedFabric-Avatar" => avatar_url }.each do |name, value|
        headers[name] = Client.escape(value) unless value.to_s.empty?
      end
      Member.new(self, headers.freeze)
    end

    # @api private
    def self.escape(value)
      ERB::Util.url_encode(value.to_s)
    end

    # @api private
    def request(method, path, headers, query: {}, body: nil)
      uri = URI(@base_url + path)
      params = query.reject { |_, v| v.nil? || v == "" }
      uri.query = URI.encode_www_form(params) unless params.empty?
      payload = body.nil? ? nil : JSON.generate(body)
      all = {
        "Authorization" => "Bearer #{@api_key}",
        "Accept" => "application/json",
        "User-Agent" => "feedfabric-ruby/#{VERSION}",
      }
      all["Content-Type"] = "application/json" if payload
      all.merge!(headers)
      # Safe to send twice: everything but POST. A 429 means nothing happened, so that's always safe.
      repeatable = method != "POST"

      attempt = 0
      loop do
        begin
          response = send_request(method, uri, all, payload)
        rescue *NETWORK_ERRORS => e
          if repeatable && attempt < @max_retries
            sleep(backoff(attempt))
            attempt += 1
            next
          end
          raise Error.new(0, "network_error", e.message)
        end

        status = response.code.to_i
        if status < 300
          text = response.body.to_s
          return text.empty? ? nil : JSON.parse(text)
        end
        error = Error.from_response(response)
        retry_it = status == 429 || (status == 503 && repeatable)
        raise error unless retry_it && attempt < @max_retries

        sleep(error.retry_after || backoff(attempt))
        attempt += 1
      end
    end

    # To the storage's own signed URL, which doesn't take the API key
    # @api private
    def put_file(url, content, content_type)
      response = send_request("PUT", URI(url), { "Content-Type" => content_type }, content)
      raise Error.from_response(response) unless response.code.to_i < 300
    rescue *NETWORK_ERRORS => e
      raise Error.new(0, "network_error", e.message)
    end

    private

    def send_request(method, uri, headers, body)
      request = Net::HTTPGenericRequest.new(method, !body.nil?, true, uri.request_uri, headers)
      request.body = body if body
      Net::HTTP.start(uri.host, uri.port, use_ssl: uri.scheme == "https", open_timeout: @timeout, read_timeout: @timeout,
                                          write_timeout: @timeout) do |http|
        http.request(request)
      end
    end

    def backoff(attempt)
      [0.25 * (2**attempt), 4.0].min + (rand / 10)
    end
  end

  # Everything a member can do, as that member
  class Member
    attr_reader :posts, :feeds, :comments, :media, :signals

    def initialize(client, headers)
      @client = client
      @headers = headers
      @posts = Posts.new(self)
      @feeds = Feeds.new(self)
      @comments = Comments.new(self)
      @media = MediaUploads.new(self, client)
      @signals = Signals.new(self)
    end

    # @api private
    def call(method, path, query: {}, body: nil)
      @client.request(method, path, @headers, query: query, body: body)
    end

    # Who FeedFabric thinks this is; also creates the member if they're new
    def me = call("GET", "/me")

    def follow(external_user_id) = call("PUT", "/follows/#{esc(external_user_id)}")

    def unfollow(external_user_id) = call("DELETE", "/follows/#{esc(external_user_id)}")

    def favourites = call("GET", "/favourites")

    # Up to 30; favourite.limit_reached after that
    def add_favourite(external_user_id) = call("PUT", "/favourites/#{esc(external_user_id)}")

    def remove_favourite(external_user_id) = call("DELETE", "/favourites/#{esc(external_user_id)}")

    # The registered data types and their schemas
    def data_types = call("GET", "/data-types")

    def data_type(key) = call("GET", "/data-types/#{esc(key)}")

    # Everything held about the member, as one JSON document. A few an hour.
    def export_account = call("GET", "/me/export")

    # Erases the member: posts, comments and profile at once, the rest shortly after
    def delete_account = call("DELETE", "/me")

    private

    def esc(value) = Client.escape(value)
  end

  # The member's own posts, and what they do with anyone's
  class Posts
    def initialize(member)
      @m = member
    end

    # content is required unless the post has data. tags are your app's own, written like hashtags without the #;
    # up to 10. data is structured data of a registered type: DataType#of builds it.
    def create(content: nil, tags: nil, media_ids: nil, data: nil)
      @m.call("POST", "/userfeed/posts", body: body(content, tags, media_ids, data))
    end

    # Within an hour of posting. Leaving tags or data out keeps the post's.
    def edit(post_id, content: nil, tags: nil, data: nil)
      @m.call("PUT", "/userfeed/posts/#{esc(post_id)}", body: body(content, tags, nil, data))
    end

    def delete(post_id) = @m.call("DELETE", "/userfeed/posts/#{esc(post_id)}")

    # The member's own posts; type keeps only those with data of that type. order is "newest" (the default) or
    # "oldest". Post IDs are time-ordered (UUIDv7): gt, gte, lt and lte bound the range.
    def list(type: nil, order: nil, limit: nil, cursor: nil, gt: nil, gte: nil, lt: nil, lte: nil)
      @m.call("GET", "/userfeed",
              query: { type: type, order: order, limit: limit, cursor: cursor, gt: gt, gte: gte, lt: lt, lte: lte })
    end

    def like(post_id) = @m.call("PUT", "/userfeed/posts/#{esc(post_id)}/like")

    def unlike(post_id) = @m.call("DELETE", "/userfeed/posts/#{esc(post_id)}/like")

    def repost(post_id) = @m.call("PUT", "/userfeed/posts/#{esc(post_id)}/repost")

    def unrepost(post_id) = @m.call("DELETE", "/userfeed/posts/#{esc(post_id)}/repost")

    # reason: "spam", "harassment", "hate", "threat", "sexual", "self_harm" or "other"
    def report(post_id, reason) = @m.call("POST", "/userfeed/posts/#{esc(post_id)}/report", body: { reason: reason })

    private

    def body(content, tags, media_ids, data)
      { content: content, tags: tags, mediaIds: media_ids, data: data }.compact
    end

    def esc(value) = Client.escape(value)
  end

  class Feeds
    def initialize(member)
      @m = member
    end

    # Posts from the accounts the member follows, newest first
    def home(limit: nil, cursor: nil) = @m.call("GET", "/userfeed/home", query: { limit: limit, cursor: cursor })

    # Ranked for the member; profile picks a ranking profile
    def for_you(profile: nil, limit: nil, cursor: nil)
      @m.call("GET", "/userfeed/foryou", query: { profile: profile, limit: limit, cursor: cursor })
    end

    def favourites(limit: nil, cursor: nil) = @m.call("GET", "/userfeed/favourites", query: { limit: limit, cursor: cursor })

    # Posts with a tag (hashtag, inferred topic or app tag), newest first
    def tag(tag, limit: nil, cursor: nil)
      @m.call("GET", "/userfeed/tags/#{Client.escape(tag)}/posts", query: { limit: limit, cursor: cursor })
    end

    def trending_tags(hours: nil, limit: nil) = @m.call("GET", "/userfeed/tags/trending", query: { hours: hours, limit: limit })

    def mentions(limit: nil, cursor: nil) = @m.call("GET", "/userfeed/mentions", query: { limit: limit, cursor: cursor })
  end

  class Comments
    def initialize(member)
      @m = member
    end

    # Top-level comments, oldest first
    def list(post_id, limit: nil, cursor: nil)
      @m.call("GET", "/userfeed/posts/#{esc(post_id)}/comments", query: { limit: limit, cursor: cursor })
    end

    def replies(comment_id, limit: nil, cursor: nil)
      @m.call("GET", "/userfeed/comments/#{esc(comment_id)}/replies", query: { limit: limit, cursor: cursor })
    end

    def create(post_id, content, parent_comment_id: nil)
      @m.call("POST", "/userfeed/posts/#{esc(post_id)}/comments", body: { content: content, parentCommentId: parent_comment_id }.compact)
    end

    def delete(comment_id) = @m.call("DELETE", "/userfeed/comments/#{esc(comment_id)}")

    def report(comment_id, reason) = @m.call("POST", "/userfeed/comments/#{esc(comment_id)}/report", body: { reason: reason })

    private

    def esc(value) = Client.escape(value)
  end

  class MediaUploads
    def initialize(member, client)
      @m = member
      @client = client
    end

    # Asks for an upload URL: then PUT the file there and call complete
    def create_upload(content_type, size) = @m.call("POST", "/media/uploads", body: { contentType: content_type, size: size })

    def complete(media_id) = @m.call("POST", "/media/#{Client.escape(media_id)}/complete")

    def get(media_id) = @m.call("GET", "/media/#{Client.escape(media_id)}")

    # The whole upload: asks for a URL, sends the file, and waits for scanning. Returns the ready image, for a post's
    # media_ids; raises FeedFabric::Error "media.rejected" if it's refused, or "media.timeout" if it's still being
    # checked after timeout seconds.
    def upload(content, content_type, timeout: 60)
      upload = create_upload(content_type, content.bytesize)
      @client.put_file(upload["uploadUrl"], content, upload["contentType"])
      media = complete(upload["mediaId"])
      deadline = Process.clock_gettime(Process::CLOCK_MONOTONIC) + timeout
      while %w[processing awaitingUpload].include?(media["status"])
        if Process.clock_gettime(Process::CLOCK_MONOTONIC) > deadline
          raise Error.new(0, "media.timeout", "The image is still being checked; try get later.", params: { "mediaId" => media["id"] })
        end

        sleep(@client.upload_poll)
        media = get(upload["mediaId"])
      end
      if media["status"] == "rejected"
        reason = media["rejectionReason"]
        raise Error.new(422, "media.rejected", "The image was rejected: #{reason || "unknown reason"}",
                        params: { "mediaId" => media["id"], "reason" => reason })
      end
      media
    end
  end

  # What the member's reading tells the ranker. Send them from your server as your app reports them.
  class Signals
    def initialize(member)
      @m = member
    end

    # Posts the member saw (at least half on screen for a second); up to 100 at a time
    def impressions(post_ids) = @m.call("POST", "/userfeed/impressions", body: { postIds: post_ids })

    # The member opened the post's own page
    def open(post_id) = @m.call("POST", "/userfeed/posts/#{Client.escape(post_id)}/open")

    # How long posts stayed on screen ([{ postId:, ms: }]); up to 100 at a time
    def dwell(views) = @m.call("POST", "/userfeed/dwell", body: { views: views })

    def not_interested(post_id) = @m.call("POST", "/userfeed/posts/#{Client.escape(post_id)}/not-interested")

    def show_more(post_id) = @m.call("POST", "/userfeed/posts/#{Client.escape(post_id)}/show-more")
  end
end
