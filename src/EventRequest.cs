using System.Net;

namespace AgentGlow
{
    internal sealed class EventRequest
    {
        public string Route;
        public string Body;
        public IPEndPoint Client;
        public int Port;
        public object Context;
    }
}
