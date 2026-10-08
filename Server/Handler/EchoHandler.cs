using System;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// P0 连通性验收处理器：echo 回环（演示时间戳语义 —— ServerTimestamp 由服务器填充）
    /// </summary>
    public class EchoHandler
    {
        private readonly Server3C _server;
        private readonly ILogger _logger;

        public EchoHandler(Server3C server, ILogger logger)
        {
            _server = server;
            _logger = logger;
        }

        public async Task HandleEchoAsync(IServerSession session, EchoRequest request)
        {
            var response = new EchoResponse
            {
                ClientTimestamp = request.ClientTimestamp,
                ServerTimestamp = DateTime.UtcNow.Ticks
            };

            await _server.SendToSessionAsync(session, MessageId.EchoResponse, response);
            _logger.LogInformation($"Echo roundtrip for session {session.SessionId}");
        }
    }
}
