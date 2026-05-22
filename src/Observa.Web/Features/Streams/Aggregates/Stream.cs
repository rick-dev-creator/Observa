using Crucible.Domain.Aggregates;
using Crucible.Domain.Attributes;
using Crucible.Domain.Errors;
using Crucible.Domain.Results;
using Observa.Features.Connectors.Domain;
using Observa.Features.Streams.Dtos;
using Observa.Features.Streams.Entities;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Errors;
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
    public ConnectorBinding? Binding { get; private set; }
    public StreamStatus Status { get; private set; } = StreamStatus.Active;

    public IReadOnlyList<FlowEvent> Events => _events;

    [Step(Order = 1, Entry = true)]
    public Result<StreamRegistered> Register(RegisterStreamDto dto)
    {
        var errors = new List<IError>();
        if (string.IsNullOrWhiteSpace(dto.Name))
            errors.Add(new ValidationError(DomainErrors.Stream.NameRequired, "Stream name is required.", nameof(dto.Name)));
        if (string.IsNullOrWhiteSpace(dto.Category))
            errors.Add(new ValidationError(DomainErrors.Stream.CategoryRequired, "Stream category is required.", nameof(dto.Category)));

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
        Binding = dto.Binding;
        Status = StreamStatus.Active;

        var evt = new StreamRegistered(Id, Name, Direction, Category);
        Raise(evt);
        return evt;
    }

    [Step(Order = 2, AllowedAfter = new[] { nameof(Register), nameof(Resume) })]
    public Result<FlowEventIngested> IngestEvent(IngestEventDto dto)
    {
        if (Status != StreamStatus.Active)
            return new BusinessRuleError(DomainErrors.Stream.NotActive, $"Stream must be Active to ingest events; current status is {Status}.");

        if (Direction == Direction.Performance)
        {
            if (dto.Amount == 0)
                return new ValidationError(DomainErrors.FlowEvent.AmountZero, "Performance event amount must be non-zero.", nameof(dto.Amount));
        }
        else if (dto.Amount <= 0)
        {
            return new ValidationError(DomainErrors.FlowEvent.AmountNotPositive, "Flow event amount must be positive.", nameof(dto.Amount));
        }

        if (dto.ExternalRef is { Length: > 0 } extRef
            && _events.Any(e => e.ExternalRef == extRef))
        {
            return new BusinessRuleError(
                DomainErrors.FlowEvent.Duplicate,
                $"Flow event with external ref '{extRef}' already ingested.");
        }

        var moneyResult = Direction == Direction.Performance
            ? Money.CreateSigned(dto.Amount)
            : Money.Create(dto.Amount);
        if (moneyResult.IsFailure)
            return Result<FlowEventIngested>.Failure(moneyResult.Errors);

        var eventId = FlowEventId.New();
        var amount = moneyResult.Value;
        var flowEvent = new FlowEvent(eventId, dto.OccurredAt, amount, dto.Source, dto.ExternalRef);
        _events.Add(flowEvent);

        var domainEvt = new FlowEventIngested(Id, eventId, amount, dto.OccurredAt);
        Raise(domainEvt);
        return domainEvt;
    }

    [Step(Order = 2, AllowedAfter = new[] { nameof(Register) })]
    public Result<StreamPaused> Pause()
    {
        if (Status != StreamStatus.Active)
            return new BusinessRuleError(DomainErrors.Stream.NotActiveForPause, $"Only Active streams can be paused; current status is {Status}.");

        Status = StreamStatus.Paused;
        var evt = new StreamPaused(Id);
        Raise(evt);
        return evt;
    }

    [Step(Order = 3, AllowedAfter = new[] { nameof(Pause) })]
    public Result<StreamResumed> Resume()
    {
        if (Status != StreamStatus.Paused)
            return new BusinessRuleError(DomainErrors.Stream.NotPausedForResume, $"Only Paused streams can be resumed; current status is {Status}.");

        Status = StreamStatus.Active;
        var evt = new StreamResumed(Id);
        Raise(evt);
        return evt;
    }

    [Step(Order = 4, AllowedAfter = new[] { nameof(Register), nameof(Pause) })]
    public Result<StreamStopped> Stop()
    {
        if (Status is StreamStatus.Stopped or StreamStatus.Deleted)
            return new BusinessRuleError(DomainErrors.Stream.AlreadyTerminal, $"Stream is already in terminal state {Status}; cannot stop.");

        Status = StreamStatus.Stopped;
        var evt = new StreamStopped(Id);
        Raise(evt);
        return evt;
    }

    [Step(Order = 4, AllowedAfter = new[] { nameof(Register), nameof(Pause) })]
    public Result<StreamDeleted> Delete()
    {
        if (Status is StreamStatus.Stopped or StreamStatus.Deleted)
            return new BusinessRuleError(DomainErrors.Stream.AlreadyTerminal, $"Stream is already in terminal state {Status}; cannot delete.");

        Status = StreamStatus.Deleted;
        var evt = new StreamDeleted(Id);
        Raise(evt);
        return evt;
    }

    [Step(Order = 2, AllowedAfter = new[] { nameof(Register), nameof(Resume) })]
    public Result<ConnectorPolled> RecordPoll(DateTimeOffset at)
    {
        if (Status != StreamStatus.Active)
            return new BusinessRuleError(DomainErrors.Stream.NotActive, $"Stream must be Active to record a poll; current status is {Status}.");
        if (Binding is null)
            return new BusinessRuleError(DomainErrors.Stream.NoBindingForPoll, "Cannot record poll on a stream without a connector binding.");

        var rebound = ConnectorBinding.Create(Binding.ConnectorId, Binding.ExternalRef, at, Binding.SnapshotState);
        if (rebound.IsFailure)
            return Result<ConnectorPolled>.Failure(rebound.Errors);
        Binding = rebound.Value;

        var evt = new ConnectorPolled(Id, at);
        Raise(evt);
        return evt;
    }
}
