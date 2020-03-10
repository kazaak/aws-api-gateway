using System;
using System.Collections.Generic;

namespace HelloS3
{
    /// <summary>
    /// result of the operation.
    /// </summary>
    public enum ResponseCode
    {
        Ok = 0,
        AwsError,
        Error
    }
    /// <summary>
    /// simple result class
    /// </summary>
    public class Result
    {
        /// <summary>
        /// ResponseCode
        /// </summary>
        public ResponseCode ResponseCode = ResponseCode.Ok;

        /// <summary>
        /// detailed error message.  Will be empty on success
        /// </summary>
        public string Message = string.Empty;
    }

    public class GetResult : Result
    {
        public IEnumerable<string> Messages;
    }
}
