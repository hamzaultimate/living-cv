namespace Portfolio.Web.Security;

/// <summary>
/// Lets one sign-in verification run at a time per app instance, so the expensive password hash cannot be used to
/// exhaust the server. Waiting is bounded: a caller that cannot get in within <see cref="Timeout"/> is turned away.
/// Register as a singleton; it is deliberately not static, so separate hosts (and tests) never share a queue.
/// </summary>
public sealed class SignInGate
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task<bool> EnterAsync(CancellationToken ct) => _semaphore.WaitAsync(Timeout, ct);

    public void Exit() => _semaphore.Release();
}
