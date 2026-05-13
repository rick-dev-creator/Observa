using Crucible.Domain.Aggregates;
using Crucible.Domain.Attributes;
using Crucible.Domain.Errors;
using Crucible.Domain.Results;
using Observa.Features.Streams.Dtos;
using Observa.Features.Streams.Entities;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Events;
using Observa.Features.Streams.Identifiers;
using Observa.Features.Streams.ValueObjects;

namespace Observa.Features.Streams.Aggregates;

[Aggregate]
public partial class Stream : AggregateRoot<StreamId>
{
    private readonly List<FlowEvent> _events = new();

    private Stream() { }

    public string Name { get; private set; } = "";
    public string Category { get; private set; } = "";
    public Direction Direction { get; private set; }
    public Recurrence? Schedule { get; private set; }
    public Money? ExpectedAmount { get; private set; }
    public StreamStatus Status { get; private set; } = StreamStatus.Active;

    public IReadOnlyList<FlowEvent> Events => _events;

    [Step(Order = 1, Entry = true)]
    public Result<StreamRegistered> Register(RegisterStreamDto dto)
    {
        var errors = new List<IError>();
        if (string.IsNullOrWhiteSpace(dto.Name))
            errors.Add(new ValidationError("STREAM_NAME_REQUIRED", "Stream name is required.", nameof(dto.Name)));
        if (string.IsNullOrWhiteSpace(dto.Category))
            errors.Add(new ValidationError("STREAM_CATEGORY_REQUIRED", "Stream category is required.", nameof(dto.Category)));

        Money? expected = null;
        if (dto.ExpectedAmount is { } expectedRaw)
        {
            var moneyResult = Money.Create(expectedRaw);
            if (moneyResult.IsFailure)
                errors.AddRange(moneyResult.Errors);
            else
                expected = moneyResult.Value;
        }

        if (errors.Count > 0) return Result<StreamRegistered>.Failure(errors);

        Id = StreamId.New();
        Name = dto.Name;
        Category = dto.Category;
        Direction = dto.Direction;
        Schedule = dto.Schedule;
        ExpectedAmount = expected;
        Status = StreamStatus.Active;

        var evt = new StreamRegistered(Id, Name, Direction, Category);
        Raise(evt);
        return evt;
    }

    [Step(Order = 2, AllowedAfter = new[] { nameof(Register) })]
    public Result<FlowEventIngested> IngestEvent(IngestEventDto dto)
    {
        if (Status != StreamStatus.Active)
            return new BusinessRuleError("STREAM_NOT_ACTIVE", $"Stream must be Active to ingest events; current status is {Status}.");
        if (dto.Amount <= 0)
            return new ValidationError("FLOW_EVENT_AMOUNT_NOT_POSITIVE", "Flow event amount must be positive.", nameof(dto.Amount));

        var moneyResult = Money.Create(dto.Amount);
        if (moneyResult.IsFailure)
            return Result<FlowEventIngested>.Failure(moneyResult.Errors);

        var eventId = FlowEventId.New();
        var amount = moneyResult.Value;
        var flowEvent = new FlowEvent(eventId, dto.OccurredAt, amount, dto.Source);
        _events.Add(flowEvent);

        var domainEvt = new FlowEventIngested(Id, eventId, amount, dto.OccurredAt);
        Raise(domainEvt);
        return domainEvt;
    }
}
