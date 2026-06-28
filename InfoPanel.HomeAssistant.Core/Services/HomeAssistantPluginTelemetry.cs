namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>Rolling counters for plugin test diagnostics.</summary>
public static class HomeAssistantPluginTelemetry
{
    private static readonly object Gate = new();
    private static DateTime _lastSummaryUtc = DateTime.UtcNow;

    private static long _wsMessagesReceived;
    private static long _wsMessagesApplied;
    private static long _wsMessagesIgnored;
    private static long _pollCycles;
    private static long _pollSkipped;
    private static long _pollAppliedEntities;
    private static long _pollElapsedMsTotal;

    public static string SubscriptionMode { get; set; } = "unknown";

    public static void RecordWsMessage(bool applied)
    {
        if (!HomeAssistantPluginLog.Enabled)
        {
            return;
        }

        lock (Gate)
        {
            _wsMessagesReceived++;
            if (applied)
            {
                _wsMessagesApplied++;
            }
            else
            {
                _wsMessagesIgnored++;
            }
        }
    }

    public static void RecordPoll(bool skipped, int appliedEntities, long elapsedMs)
    {
        if (!HomeAssistantPluginLog.Enabled)
        {
            return;
        }

        lock (Gate)
        {
            _pollCycles++;
            if (skipped)
            {
                _pollSkipped++;
            }

            _pollAppliedEntities += appliedEntities;
            _pollElapsedMsTotal += elapsedMs;
        }
    }

    public static void MaybeLogSummary()
    {
        if (!HomeAssistantPluginLog.Enabled)
        {
            return;
        }

        lock (Gate)
        {
            if (DateTime.UtcNow - _lastSummaryUtc < TimeSpan.FromSeconds(60))
            {
                return;
            }

            long polls = Math.Max(_pollCycles, 1);
            HomeAssistantPluginLog.Info(
                $"Telemetry 60s: ws_rx={_wsMessagesReceived}, ws_applied={_wsMessagesApplied}, " +
                $"ws_ignored={_wsMessagesIgnored}, poll_cycles={_pollCycles}, poll_skipped={_pollSkipped}, " +
                $"poll_applied={_pollAppliedEntities}, poll_avg_ms={_pollElapsedMsTotal / polls}, " +
                $"subscription={SubscriptionMode}");

            _wsMessagesReceived = 0;
            _wsMessagesApplied = 0;
            _wsMessagesIgnored = 0;
            _pollCycles = 0;
            _pollSkipped = 0;
            _pollAppliedEntities = 0;
            _pollElapsedMsTotal = 0;
            _lastSummaryUtc = DateTime.UtcNow;
        }
    }
}
