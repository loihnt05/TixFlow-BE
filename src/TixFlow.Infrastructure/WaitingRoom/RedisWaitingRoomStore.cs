using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using TixFlow.Application.WaitingRoom;

namespace TixFlow.Infrastructure.WaitingRoom;

public sealed class RedisWaitingRoomStore(IConnectionMultiplexer connection) : IWaitingRoomStore
{
    private const int IdleMilliseconds = 5 * 60 * 1000;
    private const int GrantMilliseconds = 2 * 60 * 1000;
    private const int EntryRetentionSeconds = 24 * 60 * 60;
    private const int SaleRetentionSeconds = 7 * 24 * 60 * 60;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string JoinScript = """
        local nowParts = redis.call('TIME')
        local now = nowParts[1] * 1000 + math.floor(nowParts[2] / 1000)
        local existing = redis.call('GET', KEYS[1])
        if existing then
            local oldKey = 'wr:entry:' .. existing
            local oldStatus = redis.call('HGET', oldKey, 'status')
            local oldExpiry = tonumber(redis.call('HGET', oldKey, 'expiresAt') or '0')
            if (oldStatus == 'Waiting' or oldStatus == 'Admitted') and oldExpiry > now then
                if oldStatus == 'Waiting' then
                    redis.call('HSET', oldKey, 'expiresAt', now + ARGV[4])
                    redis.call('PEXPIRE', KEYS[1], ARGV[4])
                    redis.call('EXPIRE', oldKey, ARGV[5])
                    redis.call('EXPIRE', KEYS[2], ARGV[6])
                    redis.call('EXPIRE', KEYS[3], ARGV[6])
                end
                local rank = redis.call('ZRANK', KEYS[3], existing)
                return cjson.encode({queueId=existing, eventSessionId=ARGV[1], status=oldStatus,
                    sequence=tonumber(redis.call('HGET', oldKey, 'sequence')),
                    joinedAtUnixMs=tonumber(redis.call('HGET', oldKey, 'joinedAt')),
                    expiresAtUnixMs=tonumber(redis.call('HGET', oldKey, 'expiresAt')),
                    positionEstimate=rank and rank + 1 or cjson.null,
                    grantId=redis.call('HGET', oldKey, 'grantId') or cjson.null})
            end
            redis.call('ZREM', KEYS[3], existing)
            redis.call('ZREM', 'wr:active:' .. ARGV[1], existing)
            redis.call('DEL', KEYS[1])
            if oldStatus == 'Waiting' or oldStatus == 'Admitted' then
                redis.call('HSET', oldKey, 'status', 'Expired')
            end
        end
        local sequence = redis.call('INCR', KEYS[2])
        redis.call('HSET', KEYS[4], 'queueId', ARGV[2], 'eventSessionId', ARGV[1],
            'subject', ARGV[3], 'subjectKey', KEYS[1], 'status', 'Waiting',
            'sequence', sequence, 'joinedAt', now, 'expiresAt', now + ARGV[4])
        redis.call('EXPIRE', KEYS[4], ARGV[5])
        redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[4])
        redis.call('ZADD', KEYS[3], sequence, ARGV[2])
        redis.call('EXPIRE', KEYS[2], ARGV[6])
        redis.call('EXPIRE', KEYS[3], ARGV[6])
        return cjson.encode({queueId=ARGV[2], eventSessionId=ARGV[1], status='Waiting',
            sequence=sequence, joinedAtUnixMs=now, expiresAtUnixMs=now + tonumber(ARGV[4]),
            positionEstimate=redis.call('ZCARD', KEYS[3]), grantId=cjson.null})
        """;

    private const string GetScript = """
        local entryKey = KEYS[1]
        if redis.call('HGET', entryKey, 'subject') ~= ARGV[1] then return '' end
        local id = redis.call('HGET', entryKey, 'queueId')
        local sale = redis.call('HGET', entryKey, 'eventSessionId')
        local subjectKey = redis.call('HGET', entryKey, 'subjectKey')
        local status = redis.call('HGET', entryKey, 'status')
        local expiry = tonumber(redis.call('HGET', entryKey, 'expiresAt') or '0')
        local nowParts = redis.call('TIME')
        local now = nowParts[1] * 1000 + math.floor(nowParts[2] / 1000)
        local queueKey = 'wr:queue:' .. sale
        if (status == 'Waiting' or status == 'Admitted') and expiry <= now then
            status = 'Expired'
            redis.call('HSET', entryKey, 'status', status)
            redis.call('ZREM', queueKey, id)
            redis.call('ZREM', 'wr:active:' .. sale, id)
            if redis.call('GET', subjectKey) == id then redis.call('DEL', subjectKey) end
        elseif status == 'Waiting' then
            expiry = now + tonumber(ARGV[2])
            redis.call('HSET', entryKey, 'expiresAt', expiry)
            redis.call('SET', subjectKey, id, 'PX', ARGV[2])
            redis.call('EXPIRE', entryKey, ARGV[3])
            redis.call('EXPIRE', queueKey, ARGV[4])
            redis.call('EXPIRE', 'wr:sequence:' .. sale, ARGV[4])
        end
        local rank = status == 'Waiting' and redis.call('ZRANK', queueKey, id) or nil
        return cjson.encode({queueId=id, eventSessionId=sale, status=status,
            sequence=tonumber(redis.call('HGET', entryKey, 'sequence')),
            joinedAtUnixMs=tonumber(redis.call('HGET', entryKey, 'joinedAt')),
            expiresAtUnixMs=expiry, positionEstimate=rank and rank + 1 or cjson.null,
            grantId=redis.call('HGET', entryKey, 'grantId') or cjson.null})
        """;

    private const string AbandonScript = """
        if redis.call('HGET', KEYS[1], 'subject') ~= ARGV[1] then return 0 end
        local id = redis.call('HGET', KEYS[1], 'queueId')
        local sale = redis.call('HGET', KEYS[1], 'eventSessionId')
        local subjectKey = redis.call('HGET', KEYS[1], 'subjectKey')
        local status = redis.call('HGET', KEYS[1], 'status')
        if status == 'Waiting' or status == 'Admitted' then
            redis.call('HSET', KEYS[1], 'status', 'Abandoned')
            redis.call('ZREM', 'wr:queue:' .. sale, id)
            redis.call('ZREM', 'wr:active:' .. sale, id)
            if redis.call('GET', subjectKey) == id then redis.call('DEL', subjectKey) end
        end
        return 1
        """;

    private const string RejectScript = """
        if redis.call('HGET', KEYS[1], 'subject') ~= ARGV[1] then return 0 end
        local status = redis.call('HGET', KEYS[1], 'status')
        if status == 'Waiting' or status == 'Admitted' then
            local id = redis.call('HGET', KEYS[1], 'queueId')
            local sale = redis.call('HGET', KEYS[1], 'eventSessionId')
            local subjectKey = redis.call('HGET', KEYS[1], 'subjectKey')
            redis.call('HSET', KEYS[1], 'status', 'Rejected')
            redis.call('ZREM', 'wr:queue:' .. sale, id)
            redis.call('ZREM', 'wr:active:' .. sale, id)
            if subjectKey and redis.call('GET', subjectKey) == id then redis.call('DEL', subjectKey) end
        end
        return 1
        """;

    private const string GrantScript = """
        local nowParts = redis.call('TIME')
        local now = nowParts[1] * 1000 + math.floor(nowParts[2] / 1000)
        local budgetKey = 'wr:budget:' .. ARGV[3] .. ':' .. nowParts[1]
        if tonumber(redis.call('GET', budgetKey) or '0') >= tonumber(ARGV[5]) then return 'BUDGET' end
        local activeKey = 'wr:active:' .. ARGV[3]
        redis.call('ZREMRANGEBYSCORE', activeKey, '-inf', now)
        if redis.call('ZCARD', activeKey) >= tonumber(ARGV[6]) then return 'CAPACITY' end
        for i = 1, 100 do
            local head = redis.call('ZRANGE', KEYS[1], 0, 0)[1]
            if not head then return '' end
            local entryKey = 'wr:entry:' .. head
            local status = redis.call('HGET', entryKey, 'status')
            local expiry = tonumber(redis.call('HGET', entryKey, 'expiresAt') or '0')
            redis.call('ZREM', KEYS[1], head)
            if status == 'Waiting' and expiry > now then
                redis.call('INCR', budgetKey)
                redis.call('EXPIRE', budgetKey, 2)
                local subjectKey = redis.call('HGET', entryKey, 'subjectKey')
                redis.call('HSET', entryKey, 'status', 'Admitted', 'grantId', ARGV[1],
                    'expiresAt', now + ARGV[2])
                redis.call('EXPIRE', entryKey, ARGV[4])
                redis.call('SET', subjectKey, head, 'PX', ARGV[2])
                redis.call('ZADD', activeKey, now + tonumber(ARGV[2]), head)
                redis.call('EXPIRE', activeKey, ARGV[7])
                return cjson.encode({queueId=head, eventSessionId=ARGV[3], status='Admitted',
                    sequence=tonumber(redis.call('HGET', entryKey, 'sequence')),
                    joinedAtUnixMs=tonumber(redis.call('HGET', entryKey, 'joinedAt')),
                    expiresAtUnixMs=now + tonumber(ARGV[2]), positionEstimate=cjson.null,
                    grantId=ARGV[1]})
            end
            if status == 'Waiting' then
                redis.call('HSET', entryKey, 'status', 'Expired')
                local subjectKey = redis.call('HGET', entryKey, 'subjectKey')
                if subjectKey and redis.call('GET', subjectKey) == head then redis.call('DEL', subjectKey) end
            end
        end
        return 'RETRY'
        """;

    private const string BindIntentScript = """
        local entry = KEYS[1]
        if redis.call('HGET', entry, 'subject') ~= ARGV[1]
            or redis.call('HGET', entry, 'eventSessionId') ~= ARGV[2]
            or redis.call('HGET', entry, 'grantId') ~= ARGV[3] then return 'INVALID' end
        local status = redis.call('HGET', entry, 'status')
        if status == 'Consumed' then
            if redis.call('HGET', entry, 'intentKeyHash') == ARGV[4]
                and redis.call('HGET', entry, 'requestHash') == ARGV[5] then return 'OK' end
            return 'CONFLICT'
        end
        if status ~= 'Admitted' then return 'INVALID' end
        local nowParts = redis.call('TIME')
        local now = nowParts[1] * 1000 + math.floor(nowParts[2] / 1000)
        if tonumber(redis.call('HGET', entry, 'expiresAt') or '0') <= now then
            redis.call('HSET', entry, 'status', 'Expired')
            redis.call('ZREM', 'wr:active:' .. ARGV[2], ARGV[7])
            return 'EXPIRED'
        end
        redis.call('HSET', entry, 'status', 'Consumed', 'intentKeyHash', ARGV[4], 'requestHash', ARGV[5])
        redis.call('ZREM', 'wr:active:' .. ARGV[2], ARGV[7])
        redis.call('EXPIRE', entry, ARGV[6])
        local subjectKey = redis.call('HGET', entry, 'subjectKey')
        if subjectKey and redis.call('GET', subjectKey) == ARGV[7] then redis.call('DEL', subjectKey) end
        return 'OK'
        """;

    public async Task<QueueEntrySnapshot> JoinAsync(Guid eventSessionId, string subject, CancellationToken cancellationToken)
    {
        string sale = eventSessionId.ToString("D");
        string id = Guid.NewGuid().ToString("D");
        RedisResult result = await connection.GetDatabase().ScriptEvaluateAsync(JoinScript,
            [SubjectKey(sale, subject), SequenceKey(sale), QueueKey(sale), EntryKey(id)],
            [sale, id, subject, IdleMilliseconds, EntryRetentionSeconds, SaleRetentionSeconds]).WaitAsync(cancellationToken);
        return Parse(result)!;
    }

    public async Task<QueueEntrySnapshot?> GetAsync(Guid queueId, string subject, CancellationToken cancellationToken)
    {
        RedisResult result = await connection.GetDatabase().ScriptEvaluateAsync(GetScript,
            [EntryKey(queueId.ToString("D"))],
            [subject, IdleMilliseconds, EntryRetentionSeconds, SaleRetentionSeconds]).WaitAsync(cancellationToken);
        return Parse(result);
    }

    public async Task<bool> AbandonAsync(Guid queueId, string subject, CancellationToken cancellationToken)
    {
        RedisResult result = await connection.GetDatabase().ScriptEvaluateAsync(AbandonScript,
            [EntryKey(queueId.ToString("D"))], [subject]).WaitAsync(cancellationToken);
        return (long)result == 1;
    }

    public async Task RejectAsync(Guid queueId, string subject, CancellationToken cancellationToken)
    {
        await connection.GetDatabase().ScriptEvaluateAsync(RejectScript,
            [EntryKey(queueId.ToString("D"))], [subject]).WaitAsync(cancellationToken);
    }

    public async Task<QueueEntrySnapshot?> GrantNextAsync(Guid eventSessionId, int maxGrantsPerSecond,
        CancellationToken cancellationToken, int maxConcurrentAdmissions = 500)
    {
        if (maxConcurrentAdmissions is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentAdmissions));
        string sale = eventSessionId.ToString("D");
        string grantId = Guid.NewGuid().ToString("D");
        while (true)
        {
            RedisResult result = await connection.GetDatabase().ScriptEvaluateAsync(GrantScript,
                [QueueKey(sale)], [grantId, GrantMilliseconds, sale, EntryRetentionSeconds,
                    maxGrantsPerSecond, maxConcurrentAdmissions, SaleRetentionSeconds]).WaitAsync(cancellationToken);
            if (result.ToString() != "RETRY")
                return result.ToString() is "BUDGET" or "CAPACITY" ? null : Parse(result);
        }
    }

    public async Task<string> BindIntentAsync(Guid queueId, Guid eventSessionId, Guid grantId, string subject,
        string idempotencyKeyHash, string requestHash, CancellationToken cancellationToken)
    {
        RedisResult result = await connection.GetDatabase().ScriptEvaluateAsync(BindIntentScript,
            [EntryKey(queueId.ToString("D"))],
            [subject, eventSessionId.ToString("D"), grantId.ToString("D"), idempotencyKeyHash,
                requestHash, EntryRetentionSeconds, queueId.ToString("D")]).WaitAsync(cancellationToken);
        return result.ToString()!;
    }

    private static QueueEntrySnapshot? Parse(RedisResult result)
    {
        string? json = result.ToString();
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<QueueEntrySnapshot>(json, JsonOptions);
    }

    private static RedisKey SubjectKey(string sale, string subject) =>
        $"wr:subject:{sale}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject))).ToLowerInvariant()}";
    private static RedisKey SequenceKey(string sale) => $"wr:sequence:{sale}";
    private static RedisKey QueueKey(string sale) => $"wr:queue:{sale}";
    private static RedisKey EntryKey(string id) => $"wr:entry:{id}";
}
