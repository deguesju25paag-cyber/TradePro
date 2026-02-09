using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Zerbitzaria.Hubs
{
    public class UpdatesHub : Hub
    {
        // Client should call JoinGroup with userId to receive position updates for that user
        public Task JoinGroup(string userId)
        {
            return Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
        }

        public Task LeaveGroup(string userId)
        {
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
        }
    }
}
