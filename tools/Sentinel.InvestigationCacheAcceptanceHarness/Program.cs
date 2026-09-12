using Sentinel.App.Services;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

InvestigationCache cache = new();

cache.Set("fresh", "value", TimeSpan.FromSeconds(5));
Assert(cache.TryGet<string>("fresh", out string? fresh) && fresh == "value",
    "Fresh cache evidence should be returned.");

cache.Set("stale", "old-value", TimeSpan.FromMilliseconds(25));
await Task.Delay(100);
Assert(!cache.TryGet<string>("stale", out _),
    "Expired investigation evidence must never be returned as current.");

cache.Set("type", "text", TimeSpan.FromSeconds(5));
Assert(!cache.TryGet<int>("type", out _),
    "Cache type mismatch must fail closed.");

cache.Set("remove", "value", TimeSpan.FromSeconds(5));
Assert(cache.Remove("remove"), "Explicit invalidation should remove an active entry.");
Assert(!cache.TryGet<string>("remove", out _), "Removed evidence must not be returned.");

cache.Set("expired-count", "value", TimeSpan.FromMilliseconds(25));
await Task.Delay(100);
int removed = cache.RemoveExpired();
Assert(removed >= 1, "Expired-entry cleanup should remove stale evidence.");

InvestigationCache.CacheStatus status = cache.GetStatus();
Assert(status.ExpiredEntryCount == 0,
    "After explicit cleanup, no expired investigation evidence should remain tracked.");

Console.WriteLine("Investigation cache acceptance harness PASS");
