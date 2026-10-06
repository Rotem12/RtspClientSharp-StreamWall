using System;
using System.Runtime.Serialization;

namespace RtspClientSharp.Rtsp
{
    [Serializable]
    public class HttpBadResponseException : Exception
    {
        public HttpBadResponseException()
        {
        }

        public HttpBadResponseException(string message) : base(message)
        {
        }

        public HttpBadResponseException(string message, Exception inner) : base(message, inner)
        {
        }

#if !NET10_0_OR_GREATER
        protected HttpBadResponseException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
#endif
    }
}
