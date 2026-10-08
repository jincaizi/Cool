using System;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// 时钟同步处理器：回显 ClientTimestamp + 服务器时钟（spec D3 样本来源）
    /// </summary>
    public class ClockSyncHandler
    {
        private readonly Server3C _server;
        private readonly ILogger _logger;

        public ClockSyncHandler(Server3C server, ILogger logger)
        {
            _server = server;
            _logger = logger;
        }

        public Task HandleClockSyncAsync(IServerSession session, ClockSyncRequest request)
        {
            var response = new Cool.Protocol.ClockSyncResponse
            {
                ClientTimestamp = request.ClientTimestamp,
                ServerTimestamp = DateTime.UtcNow.Ticks
            };
            _logger.LogInformation($"ClockSync for session {session.SessionId}");
            return _server.SendToSessionAsync(session, MessageId.ClockSyncResponse, response);
        }
    }
}
