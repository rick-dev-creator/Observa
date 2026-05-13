using Crucible.Domain.Errors;
using FluentAssertions;
using Observa.Features.Streams.Aggregates;
using Observa.Features.Streams.Errors;
using Observa.Features.Streams.Dtos;
using Observa.Features.Streams.Enums;
using Observa.Features.Streams.Events;
using Observa.Features.Streams.ValueObjects;
using Stream = Observa.Features.Streams.Aggregates.Stream;

namespace Observa.Domain.Tests.Streams;

public sealed class StreamAggregateTests
{
    private static RegisterStreamDto ValidRegisterDto(
        string name = "Salary",
        string category = "Work",
        Direction direction = Direction.Income,
        decimal? expectedAmount = 8000m) =>
        new(name, category, direction, Schedule: null, ExpectedAmount: expectedAmount);

    private static IngestEventDto ValidIngestDto(decimal amount = 100m) =>
        new(DateTimeOffset.UtcNow, amount, IngestionSource.Manual);

    [Fact]
    public void Register_WithValidDto_SetsFieldsAndActiveStatus()
    {
        var stream = Stream.__CreateForChain();

        var result = stream.Register(ValidRegisterDto("Patreon", "Content", Direction.Income, 400m));

        result.IsSuccess.Should().BeTrue();
        stream.Id.Value.Should().NotBe(Guid.Empty);
        stream.Name.Should().Be("Patreon");
        stream.Category.Should().Be("Content");
        stream.Direction.Should().Be(Direction.Income);
        stream.ExpectedAmount!.Amount.Should().Be(400m);
        stream.Status.Should().Be(StreamStatus.Active);
    }

    [Fact]
    public void Register_WithoutExpectedAmount_LeavesItNull()
    {
        var stream = Stream.__CreateForChain();

        var result = stream.Register(ValidRegisterDto(expectedAmount: null));

        result.IsSuccess.Should().BeTrue();
        stream.ExpectedAmount.Should().BeNull();
    }

    [Fact]
    public void Register_WithEmptyName_ReturnsValidationError()
    {
        var stream = Stream.__CreateForChain();

        var result = stream.Register(ValidRegisterDto(name: ""));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.ErrorCode == DomainErrors.Stream.NameRequired);
    }

    [Fact]
    public void Register_WithEmptyCategory_ReturnsValidationError()
    {
        var stream = Stream.__CreateForChain();

        var result = stream.Register(ValidRegisterDto(category: ""));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.ErrorCode == DomainErrors.Stream.CategoryRequired);
    }

    [Fact]
    public void Register_WithNegativeExpectedAmount_ReturnsValidationError()
    {
        var stream = Stream.__CreateForChain();

        var result = stream.Register(ValidRegisterDto(expectedAmount: -50m));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.ErrorCode == DomainErrors.Money.NegativeAmount);
    }

    [Fact]
    public void Register_AccumulatesAllValidationErrorsAtOnce()
    {
        var stream = Stream.__CreateForChain();

        var result = stream.Register(new RegisterStreamDto("", "", Direction.Income, null, -10m));

        result.IsFailure.Should().BeTrue();
        result.Errors.Select(e => e.ErrorCode).Should().Contain(new[]
        {
            DomainErrors.Stream.NameRequired,
            DomainErrors.Stream.CategoryRequired,
            DomainErrors.Money.NegativeAmount,
        });
    }

    [Fact]
    public void Register_RaisesStreamRegisteredEvent()
    {
        var stream = Stream.__CreateForChain();

        stream.Register(ValidRegisterDto("Blofin", "Crypto", Direction.Income));

        stream.PendingEvents.Should().HaveCount(1);
        var evt = stream.PendingEvents[0].Should().BeOfType<StreamRegistered>().Subject;
        evt.StreamId.Should().Be(stream.Id);
        evt.Name.Should().Be("Blofin");
        evt.Direction.Should().Be(Direction.Income);
        evt.Category.Should().Be("Crypto");
    }

    [Fact]
    public void IngestEvent_OnActiveStream_AppendsFlowEventAndRaisesEvent()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());

        var result = stream.IngestEvent(ValidIngestDto(250m));

        result.IsSuccess.Should().BeTrue();
        stream.Events.Should().HaveCount(1);
        stream.Events[0].Amount.Amount.Should().Be(250m);
        stream.Events[0].Source.Should().Be(IngestionSource.Manual);
        stream.PendingEvents.OfType<FlowEventIngested>().Should().ContainSingle()
            .Which.Amount.Amount.Should().Be(250m);
    }

    [Fact]
    public void IngestEvent_WithZeroAmount_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());

        var result = stream.IngestEvent(ValidIngestDto(0m));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.FlowEvent.AmountNotPositive);
        result.Errors[0].Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void IngestEvent_WithNegativeAmount_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());

        var result = stream.IngestEvent(ValidIngestDto(-1m));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.FlowEvent.AmountNotPositive);
    }

    [Fact]
    public void IngestEvent_AssignsDistinctIdsToEachEvent()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());

        stream.IngestEvent(ValidIngestDto(100m));
        stream.IngestEvent(ValidIngestDto(200m));
        stream.IngestEvent(ValidIngestDto(300m));

        stream.Events.Select(e => e.Id).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void IngestEvent_WithDuplicateExternalRef_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());

        var first = new IngestEventDto(DateTimeOffset.UtcNow, 100m, IngestionSource.Connector, "patreon-pledge-42");
        stream.IngestEvent(first);

        var duplicate = new IngestEventDto(DateTimeOffset.UtcNow.AddHours(1), 100m, IngestionSource.Connector, "patreon-pledge-42");
        var result = stream.IngestEvent(duplicate);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.FlowEvent.Duplicate);
        stream.Events.Should().HaveCount(1);
    }

    [Fact]
    public void IngestEvent_DifferentExternalRefs_DoesNotDedup()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());

        stream.IngestEvent(new IngestEventDto(DateTimeOffset.UtcNow, 100m, IngestionSource.Connector, "a"));
        var second = stream.IngestEvent(new IngestEventDto(DateTimeOffset.UtcNow, 200m, IngestionSource.Connector, "b"));

        second.IsSuccess.Should().BeTrue();
        stream.Events.Should().HaveCount(2);
    }

    [Fact]
    public void IngestEvent_NullExternalRef_NoDedup()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());

        stream.IngestEvent(new IngestEventDto(DateTimeOffset.UtcNow, 50m, IngestionSource.Manual));
        var second = stream.IngestEvent(new IngestEventDto(DateTimeOffset.UtcNow, 50m, IngestionSource.Manual));

        second.IsSuccess.Should().BeTrue();
        stream.Events.Should().HaveCount(2);
    }

    [Fact]
    public void Pause_OnActiveStream_TransitionsToPausedAndRaisesEvent()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());

        var result = stream.Pause();

        result.IsSuccess.Should().BeTrue();
        stream.Status.Should().Be(StreamStatus.Paused);
        stream.PendingEvents.OfType<StreamPaused>().Should().ContainSingle();
    }

    [Fact]
    public void Pause_OnPausedStream_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Pause();

        var result = stream.Pause();

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.Stream.NotActiveForPause);
    }

    [Fact]
    public void Resume_OnPausedStream_TransitionsBackToActive()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Pause();

        var result = stream.Resume();

        result.IsSuccess.Should().BeTrue();
        stream.Status.Should().Be(StreamStatus.Active);
        stream.PendingEvents.OfType<StreamResumed>().Should().ContainSingle();
    }

    [Fact]
    public void Resume_OnActiveStream_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());

        var result = stream.Resume();

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.Stream.NotPausedForResume);
    }

    [Fact]
    public void IngestEvent_AfterResume_Succeeds()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Pause();
        stream.Resume();

        var result = stream.IngestEvent(ValidIngestDto(150m));

        result.IsSuccess.Should().BeTrue();
        stream.Events.Should().HaveCount(1);
    }

    [Fact]
    public void IngestEvent_OnPausedStream_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Pause();

        var result = stream.IngestEvent(ValidIngestDto(150m));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.Stream.NotActive);
    }

    [Fact]
    public void Stop_OnActiveStream_TransitionsToStopped()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());

        var result = stream.Stop();

        result.IsSuccess.Should().BeTrue();
        stream.Status.Should().Be(StreamStatus.Stopped);
        stream.PendingEvents.OfType<StreamStopped>().Should().ContainSingle();
    }

    [Fact]
    public void Stop_OnPausedStream_TransitionsToStopped()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Pause();

        var result = stream.Stop();

        result.IsSuccess.Should().BeTrue();
        stream.Status.Should().Be(StreamStatus.Stopped);
    }

    [Fact]
    public void Stop_OnStoppedStream_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Stop();

        var result = stream.Stop();

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.Stream.AlreadyTerminal);
    }

    [Fact]
    public void Resume_OnStoppedStream_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Stop();

        var result = stream.Resume();

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.Stream.NotPausedForResume);
    }

    [Fact]
    public void IngestEvent_OnStoppedStream_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Stop();

        var result = stream.IngestEvent(ValidIngestDto(50m));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.Stream.NotActive);
    }

    [Fact]
    public void Delete_OnActiveStream_TransitionsToDeleted()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());

        var result = stream.Delete();

        result.IsSuccess.Should().BeTrue();
        stream.Status.Should().Be(StreamStatus.Deleted);
        stream.PendingEvents.OfType<StreamDeleted>().Should().ContainSingle();
    }

    [Fact]
    public void Delete_OnPausedStream_TransitionsToDeleted()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Pause();

        var result = stream.Delete();

        result.IsSuccess.Should().BeTrue();
        stream.Status.Should().Be(StreamStatus.Deleted);
    }

    [Fact]
    public void Delete_OnStoppedStream_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Stop();

        var result = stream.Delete();

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.Stream.AlreadyTerminal);
    }

    [Fact]
    public void Delete_OnDeletedStream_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Delete();

        var result = stream.Delete();

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.Stream.AlreadyTerminal);
    }

    [Fact]
    public void IngestEvent_OnDeletedStream_Fails()
    {
        var stream = Stream.__CreateForChain();
        stream.Register(ValidRegisterDto());
        stream.Delete();

        var result = stream.IngestEvent(ValidIngestDto(75m));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == DomainErrors.Stream.NotActive);
    }
}
