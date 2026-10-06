using System;
using System.Runtime.Serialization;

namespace RtspClientSharp.MediaParsers
{
    [Serializable]
    public class MediaPayloadParserException : Exception
    {
        public MediaPayloadParserException()
        {
        }

        public MediaPayloadParserException(string message) : base(message)
        {
        }

        public MediaPayloadParserException(string message, Exception inner) : base(message, inner)
        {
        }

#if !NET10_0_OR_GREATER
        protected MediaPayloadParserException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
#endif
    }
}
