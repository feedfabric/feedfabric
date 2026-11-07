namespace FeedFabric.Application.Services;

// Entity IDs are UUIDv7 (EntityBase): their first 48 bits are the creation time in Unix milliseconds, and Postgres
// orders uuids byte by byte, so "Id < LowerBound(t)" finds everything created before t on the primary key alone
public static class Uuid7
{
    // The smallest UUIDv7 for the given time
    public static Guid LowerBound(DateTime utc)
    {
        var ms = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        Span<byte> bytes = stackalloc byte[16];
        for (var i = 0; i < 6; i++)
        {
            bytes[i] = (byte)(ms >> (8 * (5 - i)));
        }
        bytes[6] = 0x70;
        bytes[8] = 0x80;
        return new Guid(bytes, bigEndian: true);
    }

    // When a UUIDv7 was made; null for other versions
    public static DateTime? CreatedAt(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        if (bytes[6] >> 4 != 7)
        {
            return null;
        }
        long ms = 0;
        for (var i = 0; i < 6; i++)
        {
            ms = (ms << 8) | bytes[i];
        }
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    }
}
