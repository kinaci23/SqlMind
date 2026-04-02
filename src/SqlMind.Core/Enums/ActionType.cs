namespace SqlMind.Core.Enums;

/// <summary>
/// Actions that the Policy Engine can approve for execution.
/// Mapping from RiskLevel → List&lt;ActionType&gt; is read from configuration — hard-coded IF logic is FORBIDDEN.
/// </summary>
public enum ActionType
{
    CreateTicket,
    SendNotification,
    RequestApproval,
    WarnLog,
    LogOnly
}
