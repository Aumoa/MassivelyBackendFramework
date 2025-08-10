namespace Auth.Services;

internal interface IAccesses
{
    record Configuration
    {
        public required TimeSpan AccessTimeout { get; init; } = TimeSpan.FromHours(24);
    }

    ValueTask<string?> GetAccessAsync(string provider, string code, CancellationToken cancellationToken);
}
