using Server.Models;

namespace Server.Services.DMS
{
    public class DmsConnectorManager
    {
        private readonly Dictionary<string, IDmsConnector> _connectors;

        public DmsConnectorManager(IEnumerable<IDmsConnector> connectors)
        {
            _connectors = connectors.ToDictionary(c => c.GetConnectorType(), c => c);
        }

        public IDmsConnector GetConnector(string connectorType)
        {
            if (_connectors.TryGetValue(connectorType, out var connector))
            {
                return connector;
            }

            throw new ArgumentException($"Connector type '{connectorType}' is not supported.");
        }

        public bool IsConnectorSupported(string connectorType)
        {
            return _connectors.ContainsKey(connectorType);
        }

        public IEnumerable<string> GetSupportedConnectors()
        {
            return _connectors.Keys;
        }
    }
}