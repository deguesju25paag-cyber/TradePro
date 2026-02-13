using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using TradePro.Models;

namespace TradePro.Services
{
    public class RealtimeService : IDisposable
    {
        private readonly HubConnection _connection;
        public event Action<Market>? MarketUpdated;

        public RealtimeService(string url = "http://localhost:5000/updates")
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(url)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<Market>("MarketUpdated", (m) =>
            {
                MarketUpdated?.Invoke(m);
            });
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            try
            {
                await _connection.StartAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Realtime start failed: " + ex.Message);
            }
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            try
            {
                await _connection.StopAsync(ct).ConfigureAwait(false);
            }
            catch { }
        }

        public void Dispose()
        {
            try { _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        }
    }
}
