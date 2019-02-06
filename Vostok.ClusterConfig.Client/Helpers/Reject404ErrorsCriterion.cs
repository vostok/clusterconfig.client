using Vostok.Clusterclient.Core.Criteria;
using Vostok.Clusterclient.Core.Model;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal class Reject404ErrorsCriterion : IResponseCriterion
    {
        public ResponseVerdict Decide(Response response) 
            => response.Code == ResponseCode.NotFound ? ResponseVerdict.Reject : ResponseVerdict.DontKnow;
    }
}