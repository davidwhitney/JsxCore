namespace JsxCore.Compilation;

public sealed class BuildScopedCache<T> where T : class
{
    private readonly object _gate = new();
    private string? _buildId;
    private T? _value;

    public T Get(string buildId, Func<T> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        ArgumentNullException.ThrowIfNull(create);

        // A single slot rather than a map. Everything derived from a build is asked for by the
        // current build id, and a rebuild mints a new one, so keeping the old entries only grows
        // the process: in development that is once per edit, for the life of the session.
        lock (_gate)
        {
            if (_buildId == buildId && _value is not null)
            {
                return _value;
            }

            _value = create();
            _buildId = buildId;
            return _value;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _buildId = null;
            _value = null;
        }
    }
}
