namespace CommsCheck;
using MediatR;
using Microsoft.Extensions.ObjectPool;
using System.Diagnostics.Metrics;
using System.Threading.Channels;

public class CheckCommsCommandHandler(
    ObjectPool<HashWrapper> shaPool,
    ChannelWriter<CommsCheckItemWithId> writer) :
    IRequestHandler<CheckCommsCommand, CommsCheckQuestionResponseDto>
{
    public const string MetricsMeterName = "NHS.CommChecker.CheckCommsCommandHandler";
    public const string MetricsMeterVerson = "1.0";
    public const string MetricsHandleCounterName = "CheckCommsCommandHandler_Handled_Count";
    public const string GetShaReason = "Getting id in the Comms Handler.";

    private static readonly Meter MyMeter = new(MetricsMeterName, MetricsMeterVerson);
    private static readonly Counter<long> HandledCounter = MyMeter.CreateCounter<long>(MetricsHandleCounterName);

    public async Task<CommsCheckQuestionResponseDto> Handle(
        CheckCommsCommand request,
        CancellationToken cancellationToken)
    {
        var item = GetItem(request);
        var sha = await GetSha(item);
        var itemWithId = GetItemWithId(sha, item);

        await WriteToChannel(itemWithId, cancellationToken);

        return CreateResponse(sha);
    }

    private CommsCheckQuestionResponseDto CreateResponse(string sha)
    {
        return new CommsCheckQuestionResponseDto(sha);
    }

    private void WriteCompletedMetrics()
    {
        HandledCounter.Add(1);
    }

    private async Task WriteToChannel(
        CommsCheckItemWithId itemWithId, 
        CancellationToken cancellationToken)
    {
        await writer.WriteAsync(itemWithId, cancellationToken);   
        WriteCompletedMetrics();
    }

    private CommsCheckItemWithId GetItemWithId(string sha, CommsCheckItem item )
    {
        return new CommsCheckItemWithId(sha, item);
    }
    private CommsCheckItem GetItem(CheckCommsCommand request)
    {
        return  CommsCheckItem.FromDto(request.Dto);;
    }

    private async Task<string> GetSha(CommsCheckItem item)
    {
        var wrapper = shaPool.Get();
        try
        {
            return await wrapper.GetSha(item, GetShaReason);
        }
        finally
        {
            shaPool.Return(wrapper);
        }
    }
}
