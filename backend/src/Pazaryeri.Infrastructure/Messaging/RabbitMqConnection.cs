using Microsoft.Extensions.Options;
using Pazaryeri.Application.Jobs;
using RabbitMQ.Client;

namespace Pazaryeri.Infrastructure.Messaging;

/// <summary>Tek ve uzun ömürlü RabbitMQ bağlantısı; ilk kullanımda açılır, düşerse yeniden açılır.</summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqOptions Options => options.Value;

    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;
            if (_connection is not null) await _connection.DisposeAsync();

            var o = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = o.Host,
                Port = o.Port,
                UserName = o.User,
                Password = o.Password,
                VirtualHost = o.VirtualHost,
                AutomaticRecoveryEnabled = true,
            };
            _connection = await factory.CreateConnectionAsync(ct);

            // Kuyruklar yoksa oluşturulur; varsa aynı ayarla olduğu doğrulanır.
            await using var channel = await _connection.CreateChannelAsync(cancellationToken: ct);
            await RabbitMqTopology.DeclareAsync(channel, o, ct);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
