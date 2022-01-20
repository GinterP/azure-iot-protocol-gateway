namespace Microsoft.Azure.Devices.ProtocolGateway.Http
{
    sealed class MessageBody
    {
        public MessageBody(string message)
        {
            this.Message = message;
        }

        public string Message { get; }

        public string ToJsonFormat() => "{" + $"\"{nameof(MessageBody)}\" :" + "{" + $"\"{nameof(this.Message)}\"" + " :\"" + this.Message + "\"}" + "}";
    }
}