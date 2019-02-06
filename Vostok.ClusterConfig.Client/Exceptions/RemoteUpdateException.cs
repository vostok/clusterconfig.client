using System;

namespace Vostok.ClusterConfig.Client.Exceptions
{
    internal class RemoteUpdateException : Exception
    {
        public RemoteUpdateException(string message)
            : base(message)
        {
        }

        public RemoteUpdateException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}