using System.Collections.Generic;
using System.Linq;
using Vostok.Clusterclient.Core.Model;
using Vostok.Clusterclient.Core.Retry;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal class RemoteRetryPolicy : IRetryPolicy
    {
        private static readonly HashSet<ResponseCode> AllowedCodes = new HashSet<ResponseCode>
        {
            ResponseCode.ServiceUnavailable,
            ResponseCode.TooManyRequests,
            ResponseCode.ConnectFailure
        };

        public bool NeedToRetry(Request request, RequestParameters parameters, IList<ReplicaResult> results)
        {
            return parameters.Priority == RequestPriority.Critical && 
                   results.Count > 0 && 
                   results.All(result => result.Verdict == ResponseVerdict.Reject) &&
                   results.All(result => AllowedCodes.Contains(result.Response.Code));
        }
    }
}
