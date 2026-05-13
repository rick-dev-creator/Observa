namespace Observa.Features.Streams.Errors;

public static class DomainErrors
{
    public static class Stream
    {
        public const string NameRequired = "STREAM_NAME_REQUIRED";
        public const string CategoryRequired = "STREAM_CATEGORY_REQUIRED";
        public const string NotActive = "STREAM_NOT_ACTIVE";
        public const string NotActiveForPause = "STREAM_NOT_ACTIVE_FOR_PAUSE";
        public const string NotPausedForResume = "STREAM_NOT_PAUSED_FOR_RESUME";
        public const string AlreadyTerminal = "STREAM_ALREADY_TERMINAL";
    }

    public static class FlowEvent
    {
        public const string AmountNotPositive = "FLOW_EVENT_AMOUNT_NOT_POSITIVE";
    }

    public static class Money
    {
        public const string NegativeAmount = "MONEY_NEGATIVE_AMOUNT";
    }

    public static class Recurrence
    {
        public const string MonthlyAnchorRange = "RECURRENCE_MONTHLY_ANCHOR_RANGE";
        public const string WeeklyAnchorRange = "RECURRENCE_WEEKLY_ANCHOR_RANGE";
        public const string BiweeklyAnchorRange = "RECURRENCE_BIWEEKLY_ANCHOR_RANGE";
    }
}
