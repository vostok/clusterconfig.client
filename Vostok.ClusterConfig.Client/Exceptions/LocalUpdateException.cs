using System;

namespace Vostok.ClusterConfig.Client.Exceptions
{
    internal class LocalUpdateException : Exception
    {
        public LocalUpdateException(string message)
            : base(message)
        {
        }

        public LocalUpdateException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
