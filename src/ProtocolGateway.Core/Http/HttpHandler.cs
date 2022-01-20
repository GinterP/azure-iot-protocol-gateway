
using System.Text;
using DotNetty.Buffers;
using DotNetty.Codecs.Http;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using System;
using DotNetty.Common;
using Microsoft.Azure.Devices.ProtocolGateway.Mqtt;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.ProtocolGateway.Identity;
using Microsoft.Azure.Devices.ProtocolGateway.Messaging;

namespace Microsoft.Azure.Devices.ProtocolGateway.Http
{
    public class HttpHandler : ChannelHandlerAdapter
    {
        static readonly ThreadLocalCache Cache = new ThreadLocalCache();

        sealed class ThreadLocalCache : FastThreadLocal<AsciiString>
        {
            protected override AsciiString GetInitialValue()
            {
                DateTime dateTime = DateTime.UtcNow;
                return AsciiString.Cached($"{dateTime.DayOfWeek}, {dateTime:dd MMM yyyy HH:mm:ss o}");
            }
        }

        static readonly byte[] StaticPlaintext = Encoding.UTF8.GetBytes("Hello, World!");
        static readonly int StaticPlaintextLen = StaticPlaintext.Length;
        static readonly IByteBuffer PlaintextContentBuffer = Unpooled.UnreleasableBuffer(Unpooled.DirectBuffer().WriteBytes(StaticPlaintext));
        static readonly AsciiString PlaintextClheaderValue = AsciiString.Cached($"{StaticPlaintextLen}");
        static readonly AsciiString JsonClheaderValue = AsciiString.Cached($"{JsonLen()}");

        static readonly AsciiString TypePlain = AsciiString.Cached("text/plain");
        static readonly AsciiString TypeJson = AsciiString.Cached("application/json");
        static readonly AsciiString ServerName = AsciiString.Cached("Netty");
        static readonly AsciiString ContentTypeEntity = HttpHeaderNames.ContentType;
        static readonly AsciiString DateEntity = HttpHeaderNames.Date;
        static readonly AsciiString ContentLengthEntity = HttpHeaderNames.ContentLength;
        static readonly AsciiString ServerEntity = HttpHeaderNames.Server;
        private readonly IDeviceIdentityProvider authProvider;
        private readonly MessagingBridgeFactoryFunc bridgeFactory;
        volatile ICharSequence date = Cache.Value;

        public HttpHandler(IDeviceIdentityProvider authProvider, MessagingBridgeFactoryFunc bridgeFactory)
        {
            this.authProvider = authProvider;
            this.bridgeFactory = bridgeFactory;
        }

        static int JsonLen() => Encoding.UTF8.GetBytes(NewMessage().ToJsonFormat()).Length;

        static MessageBody NewMessage() => new MessageBody("Hello, World!");

        public override void ChannelRead(IChannelHandlerContext ctx, object message)
        {
            if (message is IHttpRequest request)
            {
                try
                {
                    this.Process(ctx, request);
                }
                finally
                {
                    ReferenceCountUtil.Release(message);
                }
            }
            else
            {
                ctx.FireChannelRead(message);
            }
        }

        async Task Process(IChannelHandlerContext ctx, IHttpRequest request)
        {
            string uri = request.Uri;
            switch (uri)
            {
                case "/plaintext":
                    this.WriteResponse(ctx, PlaintextContentBuffer.Duplicate(), TypePlain, PlaintextClheaderValue);
                    break;
                case "/json":
                    byte[] json = Encoding.UTF8.GetBytes(NewMessage().ToJsonFormat());
                    this.WriteResponse(ctx, Unpooled.WrappedBuffer(json), TypeJson, JsonClheaderValue);
                    break;
                case "/test":
                    var identity = await this.authProvider.GetAsync("todo", "todo", "todo", "todo");

                    IMessagingBridge messagingBridge = await bridgeFactory(identity);
                    await messagingBridge.DeviceClient.SendAsync(iotHubMessage);
                    await messagingBridge.DeviceClient.DisposeAsync(new Exception("dispose"));

                    this.WriteResponse(ctx, PlaintextContentBuffer.Duplicate(), TypePlain, PlaintextClheaderValue);
                    break;
                default:
                    var response = new DefaultFullHttpResponse(HttpVersion.Http11, HttpResponseStatus.NotFound, Unpooled.Empty, false);
                    ctx.WriteAndFlushAsync(response);
                    ctx.CloseAsync();
                    break;
            }
        }

        void WriteResponse(IChannelHandlerContext ctx, IByteBuffer buf, ICharSequence contentType, ICharSequence contentLength)
        {
            // Build the response object.
            var response = new DefaultFullHttpResponse(HttpVersion.Http11, HttpResponseStatus.OK, buf, false);
            HttpHeaders headers = response.Headers;
            headers.Set(ContentTypeEntity, contentType);
            headers.Set(ServerEntity, ServerName);
            headers.Set(DateEntity, this.date);
            headers.Set(ContentLengthEntity, contentLength);

            // Close the non-keep-alive connection after the write operation is done.
            ctx.WriteAsync(response);
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception) => context.CloseAsync();

        public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();
    }
}
